using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using ReVerso.Data;

/// <summary>
/// Thérapie miroir : capture les mouvements de la main valide via XR Hand Tracking,
/// les reproduit en miroir sur le côté affecté, et simule les bras par IK analytique 2-os.
///
/// Fonctionnement :
/// 1. Assigner les références XRHandTrackingEvents (gauche + droite) depuis le XR Origin
/// 2. Assigner les deux rigs bras+main (SkinnedMeshRenderer + os bras + os main)
/// 3. Choisir le côté à entraîner (côté affecté = celui qui reçoit le miroir)
/// 4. En jeu : le patient pose les deux mains sur la table
/// 5. Le soignant appelle Calibrer() (via bouton UI ou commande réseau)
/// 6. Les mouvements de la main valide sont reproduits en miroir sur le côté affecté
///
/// Les bras sont simulés par IK car le hand tracking XR ne fournit pas de données
/// au-delà du poignet. La position des épaules est estimée à partir de la tête.
/// </summary>
public class MirrorTherapyHandTracking : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════
    #region Types
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Armature complète d'un bras + main.
    /// Contient les références aux os du bras (UpperArm, Forearm)
    /// et aux os de la main (26 joints dans l'ordre de XRHandJointID).
    /// </summary>
    [Serializable]
    public class ArmHandRig
    {
        [Tooltip("SkinnedMeshRenderer du modèle bras + main")]
        public SkinnedMeshRenderer meshRenderer;

        [Header("Os du bras")]
        [Tooltip("Transform du haut du bras (épaule → coude)")]
        public Transform upperArm;

        [Tooltip("Transform de l'avant-bras (coude → poignet)")]
        public Transform forearm;

        [Header("Os de la main (26 joints, dans l'ordre XRHandJointID)")]
        [Tooltip("26 Transforms dans l'ordre XRHandJointID :\n" +
                 "0:Wrist, 1:Palm,\n" +
                 "2:ThumbMeta, 3:ThumbProx, 4:ThumbDist, 5:ThumbTip,\n" +
                 "6:IndexMeta, 7:IndexProx, 8:IndexInter, 9:IndexDist, 10:IndexTip,\n" +
                 "11:MiddleMeta, 12:MiddleProx, 13:MiddleInter, 14:MiddleDist, 15:MiddleTip,\n" +
                 "16:RingMeta, 17:RingProx, 18:RingInter, 19:RingDist, 20:RingTip,\n" +
                 "21:LittleMeta, 22:LittleProx, 23:LittleInter, 24:LittleDist, 25:LittleTip\n\n" +
                 "Laisser null les joints sans os correspondant (ex: Palm, Tips).")]
        public Transform[] handBones = new Transform[JOINT_COUNT];

        /// <summary>Active/désactive le rendu du mesh.</summary>
        public void SetVisible(bool visible)
        {
            if (meshRenderer != null)
                meshRenderer.enabled = visible;
        }
    }

    /// <summary>
    /// Données de calibration capturées quand le patient pose les mains sur la table.
    /// Le plan miroir passe par l'origine, orienté selon la direction gauche-droite.
    /// </summary>
    [Serializable]
    public class CalibrationData
    {
        /// <summary>Si la calibration a été effectuée.</summary>
        public bool isCalibrated;

        /// <summary>Point central entre les deux poignets (espace monde).</summary>
        public Vector3 origin;

        /// <summary>Hauteur Y de la surface de la table (espace monde).</summary>
        public float tableHeight;

        /// <summary>Normale du plan miroir (direction gauche → droite, normalisée).</summary>
        public Vector3 mirrorNormal;

        /// <summary>Direction "avant" du patient (perpendiculaire au miroir, horizontale).</summary>
        public Vector3 forwardDirection;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Constantes
    // ════════════════════════════════════════════════════════════════════

    private const int JOINT_COUNT = 26;

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Champs sérialisés
    // ════════════════════════════════════════════════════════════════════

    [Header("═══ Configuration Thérapie ═══")]
    [Tooltip("Côté du corps à entraîner (le côté affecté qui recevra le miroir).\n" +
             "Gauche = la main DROITE est valide, ses mouvements sont mirrorés à gauche.\n" +
             "Droit  = la main GAUCHE est valide, ses mouvements sont mirrorés à droite.")]
    [SerializeField] private CoteAffecte coteEntraine = CoteAffecte.Gauche;

    [Header("═══ Tracking XR ═══")]
    [Tooltip("Composant XRHandTrackingEvents pour la main gauche (sur le XR Origin)")]
    [SerializeField] private XRHandTrackingEvents leftHandTrackingEvents;

    [Tooltip("Composant XRHandTrackingEvents pour la main droite (sur le XR Origin)")]
    [SerializeField] private XRHandTrackingEvents rightHandTrackingEvents;

    [Tooltip("Transform du XR Origin (pour convertir tracking space → world space).\n" +
             "Si null, on suppose tracking space = world space.")]
    [SerializeField] private Transform xrOriginTransform;

    [Header("═══ Rigs Bras + Main ═══")]
    [Tooltip("Armature complète du bras + main gauche")]
    [SerializeField] private ArmHandRig leftRig = new ArmHandRig();

    [Tooltip("Armature complète du bras + main droite")]
    [SerializeField] private ArmHandRig rightRig = new ArmHandRig();

    [Header("═══ Simulation des Bras — Dimensions ═══")]
    [Tooltip("Longueur du haut du bras (épaule → coude) en mètres")]
    [SerializeField] private float upperArmLength = 0.28f;

    [Tooltip("Longueur de l'avant-bras (coude → poignet) en mètres")]
    [SerializeField] private float forearmLength = 0.25f;

    [Header("═══ Simulation des Bras — Épaules ═══")]
    [Tooltip("Transform de la tête/caméra VR (pour estimer la position des épaules)")]
    [SerializeField] private Transform xrHead;

    [Tooltip("Demi-largeur des épaules depuis le centre du corps (mètres)")]
    [SerializeField] private float shoulderHalfWidth = 0.18f;

    [Tooltip("Décalage vertical de l'épaule par rapport à la tête (négatif = en dessous)")]
    [SerializeField] private float shoulderDropFromHead = -0.35f;

    [Tooltip("Décalage avant/arrière de l'épaule (négatif = derrière la tête)")]
    [SerializeField] private float shoulderForwardOffset = -0.05f;

    [Header("═══ Simulation des Bras — IK ═══")]
    [Tooltip("Direction préférée du coude (pole hint) en espace local de la tête.\n" +
             "Le X est inversé automatiquement pour le côté gauche.")]
    [SerializeField] private Vector3 elbowPoleOffset = new Vector3(0f, -1f, -0.5f);

    [Tooltip("Axe local du bone qui pointe le long de la longueur de l'os.\n" +
             "Z+ = convention Unity, Y+ = convention Blender typique.")]
    [SerializeField] private Vector3 boneForwardAxis = Vector3.forward;

    [Header("═══ Lissage ═══")]
    [Tooltip("Activer le lissage des mouvements pour réduire le jitter")]
    [SerializeField] private bool enableSmoothing = true;

    [Tooltip("Vitesse de lissage des positions (plus élevé = plus réactif)")]
    [Range(5f, 50f)]
    [SerializeField] private float positionSmoothSpeed = 20f;

    [Tooltip("Vitesse de lissage des rotations")]
    [Range(5f, 50f)]
    [SerializeField] private float rotationSmoothSpeed = 15f;

    [Header("═══ Options ═══")]
    [Tooltip("Masquer les visuels de mains XRI par défaut quand le script est actif")]
    [SerializeField] private bool hideDefaultHandVisuals = true;

    [Tooltip("GameObjects des visuels de mains XRI à masquer (LeftHandQuestVisual, etc.)")]
    [SerializeField] private GameObject[] defaultHandVisuals;

    [Tooltip("Afficher les Gizmos de debug dans l'éditeur (origine, plan miroir, épaules)")]
    [SerializeField] private bool showDebugGizmos = true;

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region État privé
    // ════════════════════════════════════════════════════════════════════

    private CalibrationData calibration = new CalibrationData();

    // État du tracking
    private bool leftHandTracked;
    private bool rightHandTracked;

    // Poses des joints en espace monde (mises à jour par les events)
    private Pose[] leftJointPoses = new Pose[JOINT_COUNT];
    private Pose[] rightJointPoses = new Pose[JOINT_COUNT];
    private bool[] leftJointValid = new bool[JOINT_COUNT];
    private bool[] rightJointValid = new bool[JOINT_COUNT];

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Propriétés publiques
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Données de calibration actuelles (lecture seule).</summary>
    public CalibrationData CurrentCalibration => calibration;

    /// <summary>Le système est-il calibré ?</summary>
    public bool IsCalibrated => calibration.isCalibrated;

    /// <summary>La main valide (non affectée) est-elle trackée ?</summary>
    public bool ValidHandIsTracked =>
        coteEntraine == CoteAffecte.Gauche ? rightHandTracked : leftHandTracked;

    /// <summary>La main affectée est-elle trackée ? (utile pour la calibration)</summary>
    public bool AffectedHandIsTracked =>
        coteEntraine == CoteAffecte.Gauche ? leftHandTracked : rightHandTracked;

    /// <summary>Les deux mains sont-elles trackées ? (nécessaire pour calibrer)</summary>
    public bool BothHandsTracked => leftHandTracked && rightHandTracked;

    /// <summary>Côté entraîné (côté affecté).</summary>
    public CoteAffecte CoteEntraine
    {
        get => coteEntraine;
        set => coteEntraine = value;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Cycle de vie Unity
    // ════════════════════════════════════════════════════════════════════

    private void OnEnable()
    {
        SubscribeEvents();

        if (hideDefaultHandVisuals)
            SetDefaultVisualsActive(false);

        // Masquer les rigs tant que pas calibré
        leftRig?.SetVisible(false);
        rightRig?.SetVisible(false);

        Debug.Log("[MirrorTherapy] Script activé. En attente de calibration...");
    }

    private void OnDisable()
    {
        UnsubscribeEvents();

        if (hideDefaultHandVisuals)
            SetDefaultVisualsActive(true);
    }

    private void LateUpdate()
    {
        if (!calibration.isCalibrated) return;

        // Déterminer quel côté est valide vs affecté
        bool validIsRight = (coteEntraine == CoteAffecte.Gauche);

        Pose[] validPoses = validIsRight ? rightJointPoses : leftJointPoses;
        bool[] validJoints = validIsRight ? rightJointValid : leftJointValid;
        bool validTracked = validIsRight ? rightHandTracked : leftHandTracked;

        ArmHandRig validRig = validIsRight ? rightRig : leftRig;
        ArmHandRig affectedRig = validIsRight ? leftRig : rightRig;

        // Afficher/masquer les rigs selon l'état du tracking
        validRig?.SetVisible(validTracked);
        affectedRig?.SetVisible(validTracked);

        if (!validTracked) return;

        float dt = Time.deltaTime;

        // ── Côté valide : tracking direct + bras simulé par IK ──
        ApplyDirectTracking(validRig, validPoses, validJoints, !validIsRight, dt);

        // ── Côté affecté : tracking miroir + bras simulé par IK ──
        ApplyMirroredTracking(affectedRig, validPoses, validJoints, validIsRight, dt);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region API publique — Calibration
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calibration de la table et du plan miroir.
    /// Le patient doit avoir les deux mains posées à plat sur la table.
    /// Le soignant appelle cette méthode (bouton UI, commande réseau, etc.).
    ///
    /// Capture :
    /// - Le point d'origine (milieu entre les deux poignets)
    /// - La hauteur de la table (Y moyen des poignets)
    /// - Le plan miroir (plan sagittal passant par l'origine)
    /// </summary>
    /// <returns>true si la calibration a réussi, false sinon.</returns>
    [ContextMenu("Calibrer (les deux mains doivent être trackées)")]
    public bool Calibrer()
    {
        if (!leftHandTracked || !rightHandTracked)
        {
            Debug.LogWarning("[MirrorTherapy] Calibration impossible : les deux mains doivent être trackées !");
            return false;
        }

        // Récupérer les positions des poignets en espace monde
        Vector3 leftWrist = leftJointPoses[XRHandJointID.Wrist.ToIndex()].position;
        Vector3 rightWrist = rightJointPoses[XRHandJointID.Wrist.ToIndex()].position;

        // Point central = milieu des deux poignets
        calibration.origin = (leftWrist + rightWrist) * 0.5f;

        // Hauteur de la table = la composante Y de l'origine
        calibration.tableHeight = calibration.origin.y;

        // Normale du plan miroir = direction gauche → droite, projetée sur l'horizontale
        Vector3 leftToRight = rightWrist - leftWrist;
        leftToRight.y = 0f;

        if (leftToRight.sqrMagnitude < 0.001f)
        {
            Debug.LogError("[MirrorTherapy] Calibration échouée : les deux poignets sont trop proches !");
            return false;
        }

        calibration.mirrorNormal = leftToRight.normalized;

        // Direction avant = perpendiculaire à la normale du miroir, dans le plan horizontal
        calibration.forwardDirection = Vector3.Cross(Vector3.up, calibration.mirrorNormal).normalized;

        calibration.isCalibrated = true;

        // Activer les rigs
        leftRig?.SetVisible(true);
        rightRig?.SetVisible(true);

        Debug.Log($"[MirrorTherapy] ✓ Calibration réussie !\n" +
                  $"  Origine : {calibration.origin}\n" +
                  $"  Hauteur table : {calibration.tableHeight:F3}m\n" +
                  $"  Normale miroir : {calibration.mirrorNormal}\n" +
                  $"  Direction avant : {calibration.forwardDirection}");

        return true;
    }

    /// <summary>
    /// Réinitialise la calibration. Les rigs sont masqués.
    /// </summary>
    [ContextMenu("Reset Calibration")]
    public void ResetCalibration()
    {
        calibration.isCalibrated = false;
        leftRig?.SetVisible(false);
        rightRig?.SetVisible(false);
        Debug.Log("[MirrorTherapy] Calibration réinitialisée.");
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Abonnement aux événements de tracking
    // ════════════════════════════════════════════════════════════════════

    private void SubscribeEvents()
    {
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.AddListener(OnLeftJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.AddListener(OnLeftTrackingAcquired);
            leftHandTrackingEvents.trackingLost.AddListener(OnLeftTrackingLost);
            Debug.Log("[MirrorTherapy] ✓ Abonné aux événements main gauche");
        }

        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.AddListener(OnRightJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.AddListener(OnRightTrackingAcquired);
            rightHandTrackingEvents.trackingLost.AddListener(OnRightTrackingLost);
            Debug.Log("[MirrorTherapy] ✓ Abonné aux événements main droite");
        }
    }

    private void UnsubscribeEvents()
    {
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.RemoveListener(OnLeftJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.RemoveListener(OnLeftTrackingAcquired);
            leftHandTrackingEvents.trackingLost.RemoveListener(OnLeftTrackingLost);
        }

        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.RemoveListener(OnRightJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.RemoveListener(OnRightTrackingAcquired);
            rightHandTrackingEvents.trackingLost.RemoveListener(OnRightTrackingLost);
        }
    }

    // ─── Handlers nommés (nécessaires pour un RemoveListener propre) ───

    private void OnLeftTrackingAcquired()
    {
        leftHandTracked = true;
        Debug.Log("[MirrorTherapy] Main gauche détectée");
    }

    private void OnLeftTrackingLost()
    {
        leftHandTracked = false;
        Debug.Log("[MirrorTherapy] Main gauche perdue");
    }

    private void OnRightTrackingAcquired()
    {
        rightHandTracked = true;
        Debug.Log("[MirrorTherapy] Main droite détectée");
    }

    private void OnRightTrackingLost()
    {
        rightHandTracked = false;
        Debug.Log("[MirrorTherapy] Main droite perdue");
    }

    private void OnLeftJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        CacheJointPoses(args.hand, leftJointPoses, leftJointValid);
    }

    private void OnRightJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        CacheJointPoses(args.hand, rightJointPoses, rightJointValid);
    }

    /// <summary>
    /// Cache toutes les poses des joints d'une main en espace monde.
    /// Appelée à chaque mise à jour du tracking (event-driven).
    /// </summary>
    private void CacheJointPoses(XRHand hand, Pose[] poses, bool[] valid)
    {
        for (int i = 0; i < JOINT_COUNT; i++)
        {
            XRHandJointID jointId = XRHandJointIDUtility.FromIndex(i);
            XRHandJoint joint = hand.GetJoint(jointId);

            if (joint.TryGetPose(out Pose trackingPose))
            {
                poses[i] = TrackingToWorld(trackingPose);
                valid[i] = true;
            }
            else
            {
                valid[i] = false;
            }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Logique principale — Application des poses
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applique le tracking DIRECT (sans miroir) sur le rig du côté valide.
    /// Les os de la main reçoivent les poses trackées.
    /// Les os du bras sont simulés par IK.
    /// </summary>
    private void ApplyDirectTracking(ArmHandRig rig, Pose[] poses, bool[] validJoints,
                                      bool isLeftSide, float dt)
    {
        if (rig == null) return;

        // ── Bras simulé par IK ──
        int wristIdx = XRHandJointID.Wrist.ToIndex();
        if (validJoints[wristIdx])
        {
            Vector3 wristPos = poses[wristIdx].position;
            ApplyArmIK(rig, isLeftSide, wristPos, dt);
        }

        // ── Os de la main : poses directes depuis le tracking ──
        for (int i = 0; i < JOINT_COUNT; i++)
        {
            if (!validJoints[i]) continue;
            if (i >= rig.handBones.Length || rig.handBones[i] == null) continue;

            SetBonePose(rig.handBones[i], poses[i].position, poses[i].rotation, dt);
        }
    }

    /// <summary>
    /// Applique le tracking MIROIR sur le rig du côté affecté.
    /// Chaque pose est réfléchie à travers le plan miroir calibré.
    /// Les os du bras sont simulés par IK vers le poignet miroir.
    /// </summary>
    private void ApplyMirroredTracking(ArmHandRig rig, Pose[] validPoses, bool[] validJoints,
                                        bool isLeftSide, float dt)
    {
        if (rig == null) return;

        // ── Bras simulé par IK vers le poignet miroir ──
        int wristIdx = XRHandJointID.Wrist.ToIndex();
        if (validJoints[wristIdx])
        {
            Vector3 mirroredWrist = MirrorPosition(validPoses[wristIdx].position);
            ApplyArmIK(rig, isLeftSide, mirroredWrist, dt);
        }

        // ── Os de la main : poses miroir ──
        for (int i = 0; i < JOINT_COUNT; i++)
        {
            if (!validJoints[i]) continue;
            if (i >= rig.handBones.Length || rig.handBones[i] == null) continue;

            Vector3 mirroredPos = MirrorPosition(validPoses[i].position);
            Quaternion mirroredRot = MirrorRotation(validPoses[i].rotation);

            SetBonePose(rig.handBones[i], mirroredPos, mirroredRot, dt);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Simulation des bras — IK analytique 2-os
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Simule le bras complet via IK 2-os et applique les poses
    /// aux os UpperArm et Forearm du rig.
    /// </summary>
    private void ApplyArmIK(ArmHandRig rig, bool isLeftSide, Vector3 wristWorldPos, float dt)
    {
        if (rig.upperArm == null && rig.forearm == null) return;

        // Position de l'épaule (estimée depuis la tête)
        Vector3 shoulder = EstimateShoulderPosition(isLeftSide);

        // Direction du pole hint (direction préférée du coude)
        Vector3 poleWorld = ComputePolDirection(isLeftSide);

        // Résoudre l'IK : trouver la position du coude
        Vector3 elbow = SolveTwoBoneIK(shoulder, wristWorldPos, upperArmLength, forearmLength, poleWorld);

        // Vecteur "up" pour orienter les os du bras
        Vector3 chainDir = (wristWorldPos - shoulder).normalized;
        Vector3 upHint = Vector3.Cross(chainDir, Vector3.Cross(poleWorld, chainDir)).normalized;
        if (upHint.sqrMagnitude < 0.001f) upHint = Vector3.up;

        // UpperArm : position = épaule, regarde vers le coude
        if (rig.upperArm != null)
        {
            Vector3 upperDir = (elbow - shoulder).normalized;
            if (upperDir.sqrMagnitude > 0.0001f)
            {
                Quaternion upperRot = ComputeBoneRotation(upperDir, upHint);
                SetBonePose(rig.upperArm, shoulder, upperRot, dt);
            }
        }

        // Forearm : position = coude, regarde vers le poignet
        if (rig.forearm != null)
        {
            Vector3 foreDir = (wristWorldPos - elbow).normalized;
            if (foreDir.sqrMagnitude > 0.0001f)
            {
                Quaternion foreRot = ComputeBoneRotation(foreDir, upHint);
                SetBonePose(rig.forearm, elbow, foreRot, dt);
            }
        }
    }

    /// <summary>
    /// Résout un IK analytique 2-os (épaule → coude → poignet).
    /// Utilise la loi des cosinus pour trouver l'angle à l'épaule,
    /// puis projette le pole hint pour déterminer le plan du coude.
    /// </summary>
    /// <param name="shoulder">Position de l'épaule (monde)</param>
    /// <param name="wristTarget">Position cible du poignet (monde)</param>
    /// <param name="upperLen">Longueur haut du bras</param>
    /// <param name="foreLen">Longueur avant-bras</param>
    /// <param name="poleDirection">Direction préférée du coude (monde)</param>
    /// <returns>Position du coude (monde)</returns>
    private Vector3 SolveTwoBoneIK(Vector3 shoulder, Vector3 wristTarget,
                                    float upperLen, float foreLen, Vector3 poleDirection)
    {
        Vector3 toTarget = wristTarget - shoulder;
        float dist = toTarget.magnitude;
        float maxReach = upperLen + foreLen;

        // Bras complètement tendu
        if (dist >= maxReach * 0.999f)
            return shoulder + toTarget.normalized * upperLen;

        // Cible trop proche (replié au maximum)
        float minReach = Mathf.Abs(upperLen - foreLen);
        if (dist <= minReach + 0.001f)
            dist = minReach + 0.002f;

        // Loi des cosinus : angle à l'épaule
        float cosAngle = (upperLen * upperLen + dist * dist - foreLen * foreLen)
                         / (2f * upperLen * dist);
        cosAngle = Mathf.Clamp(cosAngle, -1f, 1f);
        float angle = Mathf.Acos(cosAngle);

        // Repère orthogonal autour de la direction épaule → cible
        Vector3 fwd = toTarget / dist;

        // Projeter le pole hint sur le plan perpendiculaire à la direction cible
        Vector3 projected = (poleDirection - Vector3.Dot(poleDirection, fwd) * fwd).normalized;

        // Fallback si le pole hint est parallèle à la direction cible
        if (projected.sqrMagnitude < 0.001f)
        {
            projected = Vector3.Cross(fwd, Vector3.up).normalized;
            if (projected.sqrMagnitude < 0.001f)
                projected = Vector3.Cross(fwd, Vector3.right).normalized;
        }

        // Position du coude sur le cercle de solutions
        return shoulder
               + fwd * (upperLen * Mathf.Cos(angle))
               + projected * (upperLen * Mathf.Sin(angle));
    }

    /// <summary>
    /// Estime la position de l'épaule à partir de la position de la tête.
    /// Utilise uniquement le yaw (rotation Y) de la tête pour garder
    /// les épaules horizontales même si le patient penche la tête.
    /// </summary>
    private Vector3 EstimateShoulderPosition(bool isLeft)
    {
        if (xrHead != null)
        {
            Vector3 headPos = xrHead.position;
            // Utiliser uniquement le yaw pour garder les épaules stables
            Quaternion headYaw = Quaternion.Euler(0f, xrHead.eulerAngles.y, 0f);
            float xOffset = isLeft ? -shoulderHalfWidth : shoulderHalfWidth;
            Vector3 localOffset = new Vector3(xOffset, shoulderDropFromHead, shoulderForwardOffset);
            return headPos + headYaw * localOffset;
        }

        // Fallback sans référence tête : utiliser l'origine de calibration
        float side = isLeft ? -shoulderHalfWidth : shoulderHalfWidth;
        return calibration.origin
               + calibration.mirrorNormal * side
               + Vector3.up * 0.4f; // Estimation grossière
    }

    /// <summary>
    /// Calcule la direction du pole hint IK en espace monde.
    /// Le X du poleOffset est inversé pour le côté gauche.
    /// </summary>
    private Vector3 ComputePolDirection(bool isLeft)
    {
        if (xrHead != null)
        {
            float poleSide = isLeft ? -1f : 1f;
            Vector3 localPole = new Vector3(
                poleSide * elbowPoleOffset.x,
                elbowPoleOffset.y,
                elbowPoleOffset.z
            );
            return xrHead.TransformDirection(localPole);
        }

        return elbowPoleOffset.normalized;
    }

    /// <summary>
    /// Calcule la rotation d'un os du bras pour qu'il pointe dans la direction donnée.
    /// Prend en compte l'axe local configuré (boneForwardAxis).
    /// </summary>
    private Quaternion ComputeBoneRotation(Vector3 direction, Vector3 upHint)
    {
        if (direction.sqrMagnitude < 0.0001f) return Quaternion.identity;

        Quaternion lookRot = Quaternion.LookRotation(direction, upHint);

        // Correction d'axe si le modèle n'utilise pas Z+ comme direction du bone
        if (boneForwardAxis != Vector3.forward)
        {
            Quaternion axisCorrection = Quaternion.FromToRotation(boneForwardAxis, Vector3.forward);
            lookRot *= axisCorrection;
        }

        return lookRot;
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Mathématiques du miroir
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Réfléchit une position à travers le plan miroir calibré.
    /// Le plan passe par calibration.origin avec la normale calibration.mirrorNormal.
    /// </summary>
    private Vector3 MirrorPosition(Vector3 worldPos)
    {
        Vector3 toPos = worldPos - calibration.origin;
        float dot = Vector3.Dot(toPos, calibration.mirrorNormal);
        return worldPos - 2f * dot * calibration.mirrorNormal;
    }

    /// <summary>
    /// Réfléchit une rotation à travers le plan miroir calibré.
    /// Réfléchit les vecteurs forward et up, puis reconstruit la rotation.
    /// Cela change automatiquement la chiralité (main gauche ↔ main droite).
    /// </summary>
    private Quaternion MirrorRotation(Quaternion rot)
    {
        Vector3 normal = calibration.mirrorNormal;

        // Réfléchir les axes forward et up de la rotation
        Vector3 fwd = Vector3.Reflect(rot * Vector3.forward, normal);
        Vector3 up = Vector3.Reflect(rot * Vector3.up, normal);

        // Sécurité : vérifier que le forward n'est pas dégénéré
        if (fwd.sqrMagnitude < 0.0001f) return rot;

        return Quaternion.LookRotation(fwd, up);
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Utilitaires
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Convertit une pose de l'espace de tracking (session space)
    /// vers l'espace monde, en appliquant la transformation du XR Origin.
    /// </summary>
    private Pose TrackingToWorld(Pose trackingPose)
    {
        if (xrOriginTransform == null)
            return trackingPose;

        return new Pose(
            xrOriginTransform.TransformPoint(trackingPose.position),
            xrOriginTransform.rotation * trackingPose.rotation
        );
    }

    /// <summary>
    /// Applique une pose (position + rotation) à un bone Transform,
    /// avec lissage optionnel pour réduire le jitter.
    /// </summary>
    private void SetBonePose(Transform bone, Vector3 position, Quaternion rotation, float dt)
    {
        if (bone == null) return;

        if (enableSmoothing)
        {
            float posFactor = Mathf.Clamp01(positionSmoothSpeed * dt);
            float rotFactor = Mathf.Clamp01(rotationSmoothSpeed * dt);
            bone.position = Vector3.Lerp(bone.position, position, posFactor);
            bone.rotation = Quaternion.Slerp(bone.rotation, rotation, rotFactor);
        }
        else
        {
            bone.position = position;
            bone.rotation = rotation;
        }
    }

    /// <summary>Active ou désactive les visuels de mains par défaut du XRI.</summary>
    private void SetDefaultVisualsActive(bool active)
    {
        if (defaultHandVisuals == null) return;
        foreach (var visual in defaultHandVisuals)
        {
            if (visual != null) visual.SetActive(active);
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    #region Debug — Gizmos
    // ════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !calibration.isCalibrated) return;

        // ── Point d'origine ──
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(calibration.origin, 0.03f);
        Gizmos.DrawSphere(calibration.origin, 0.01f);

        // ── Plan miroir (ligne sur le plan) ──
        Gizmos.color = Color.cyan;
        Vector3 lineDir = calibration.forwardDirection;
        Gizmos.DrawLine(
            calibration.origin - lineDir * 0.3f + Vector3.up * 0.001f,
            calibration.origin + lineDir * 0.3f + Vector3.up * 0.001f
        );
        // Ligne verticale du plan miroir
        Gizmos.DrawLine(
            calibration.origin - Vector3.up * 0.1f,
            calibration.origin + Vector3.up * 0.4f
        );

        // ── Normale du miroir (flèche rouge) ──
        Gizmos.color = Color.red;
        Gizmos.DrawRay(calibration.origin, calibration.mirrorNormal * 0.15f);

        // ── Direction avant du patient (flèche bleue) ──
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(calibration.origin, calibration.forwardDirection * 0.15f);

        // ── Surface de la table (rectangle semi-transparent) ──
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Vector3 tableCenter = new Vector3(calibration.origin.x, calibration.tableHeight, calibration.origin.z);
        // Construire la rotation du rectangle selon le forward du patient
        Quaternion tableRot = Quaternion.LookRotation(calibration.forwardDirection, Vector3.up);
        Gizmos.matrix = Matrix4x4.TRS(tableCenter, tableRot, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, new Vector3(0.6f, 0.002f, 0.4f));
        Gizmos.matrix = Matrix4x4.identity;

        // ── Épaules estimées ──
        if (xrHead != null)
        {
            Gizmos.color = Color.green;
            Vector3 leftShoulder = EstimateShoulderPosition(true);
            Vector3 rightShoulder = EstimateShoulderPosition(false);
            Gizmos.DrawWireSphere(leftShoulder, 0.025f);
            Gizmos.DrawWireSphere(rightShoulder, 0.025f);

            // Ligne entre les épaules
            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.DrawLine(leftShoulder, rightShoulder);
        }
    }

    #endregion
}
