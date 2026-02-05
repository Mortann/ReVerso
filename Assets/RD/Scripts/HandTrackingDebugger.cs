using System.Text;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>
/// Script de débogage pour capturer les données de tracking des mains.
/// S'abonne aux événements de XRHandTrackingEvents et expose les données.
/// Utiliser HandTrackingDebuggerUI pour afficher les données dans une UI personnalisée.
/// </summary>
public class HandTrackingDebugger : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("Référence au composant XRHandTrackingEvents pour la main gauche")]
    [SerializeField] private XRHandTrackingEvents leftHandTrackingEvents;
    
    [Tooltip("Référence au composant XRHandTrackingEvents pour la main droite")]
    [SerializeField] private XRHandTrackingEvents rightHandTrackingEvents;

    [Header("Options de débogage Console")]
    [Tooltip("Afficher les logs dans la console Unity")]
    [SerializeField] private bool logToConsole = false;
    
    [Tooltip("Fréquence des logs (1 = chaque frame, 30 = toutes les 30 frames)")]
    [SerializeField] private int logFrequency = 30;
    
    [Tooltip("Afficher tous les 26 joints dans la console (verbose)")]
    [SerializeField] private bool logAllJoints = false;

    // Données actuelles des mains (exposées publiquement)
    private HandData leftHandData = new HandData("Main Gauche");
    private HandData rightHandData = new HandData("Main Droite");
    
    /// <summary>
    /// Données de la main gauche (lecture seule)
    /// </summary>
    public HandData LeftHandData => leftHandData;
    
    /// <summary>
    /// Données de la main droite (lecture seule)
    /// </summary>
    public HandData RightHandData => rightHandData;
    
    private int frameCounter = 0;
    private StringBuilder stringBuilder = new StringBuilder();

    /// <summary>
    /// Structure pour stocker les données d'une main de manière lisible
    /// </summary>
    [System.Serializable]
    public class HandData
    {
        public string handName;
        public bool isTracked;
        public Vector3 wristPosition;
        public Quaternion wristRotation;
        public Vector3 wristEulerAngles;
        
        // Positions des bouts des doigts
        public Vector3 thumbTipPosition;
        public Vector3 indexTipPosition;
        public Vector3 middleTipPosition;
        public Vector3 ringTipPosition;
        public Vector3 littleTipPosition;
        
        // Toutes les poses des joints (pour un debug complet)
        public JointPoseData[] allJoints = new JointPoseData[26];

        public HandData(string name)
        {
            handName = name;
            for (int i = 0; i < allJoints.Length; i++)
            {
                allJoints[i] = new JointPoseData();
            }
        }
    }

    [System.Serializable]
    public class JointPoseData
    {
        public string jointName;
        public Vector3 position;
        public Quaternion rotation;
        public bool isValid;
    }

    private void OnEnable()
    {
        // S'abonner aux événements de la main gauche
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.AddListener(OnLeftHandJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.AddListener(OnLeftHandTrackingAcquired);
            leftHandTrackingEvents.trackingLost.AddListener(OnLeftHandTrackingLost);
            Debug.Log("<color=green>[HandTrackingDebugger]</color> ✓ Abonné aux événements de la main gauche");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[HandTrackingDebugger]</color> ⚠ Pas de XRHandTrackingEvents assigné pour la main gauche !");
        }

        // S'abonner aux événements de la main droite
        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.AddListener(OnRightHandJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.AddListener(OnRightHandTrackingAcquired);
            rightHandTrackingEvents.trackingLost.AddListener(OnRightHandTrackingLost);
            Debug.Log("<color=green>[HandTrackingDebugger]</color> ✓ Abonné aux événements de la main droite");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[HandTrackingDebugger]</color> ⚠ Pas de XRHandTrackingEvents assigné pour la main droite !");
        }
    }

    private void OnDisable()
    {
        // Se désabonner des événements de la main gauche
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.RemoveListener(OnLeftHandJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.RemoveListener(OnLeftHandTrackingAcquired);
            leftHandTrackingEvents.trackingLost.RemoveListener(OnLeftHandTrackingLost);
        }

        // Se désabonner des événements de la main droite
        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.RemoveListener(OnRightHandJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.RemoveListener(OnRightHandTrackingAcquired);
            rightHandTrackingEvents.trackingLost.RemoveListener(OnRightHandTrackingLost);
        }
    }

    private void Update()
    {
        frameCounter++;
    }

    #region Event Handlers - Main Gauche
    
    private void OnLeftHandTrackingAcquired()
    {
        leftHandData.isTracked = true;
        Debug.Log("<color=cyan>[HandTrackingDebugger]</color> 👋 Main GAUCHE détectée !");
    }

    private void OnLeftHandTrackingLost()
    {
        leftHandData.isTracked = false;
        Debug.Log("<color=orange>[HandTrackingDebugger]</color> ✋ Main GAUCHE perdue !");
    }

    private void OnLeftHandJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        UpdateHandData(args.hand, leftHandData);
        
        if (logToConsole && frameCounter % logFrequency == 0)
        {
            LogHandData(leftHandData);
        }
    }

    #endregion

    #region Event Handlers - Main Droite
    
    private void OnRightHandTrackingAcquired()
    {
        rightHandData.isTracked = true;
        Debug.Log("<color=cyan>[HandTrackingDebugger]</color> 👋 Main DROITE détectée !");
    }

    private void OnRightHandTrackingLost()
    {
        rightHandData.isTracked = false;
        Debug.Log("<color=orange>[HandTrackingDebugger]</color> ✋ Main DROITE perdue !");
    }

    private void OnRightHandJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        UpdateHandData(args.hand, rightHandData);
        
        if (logToConsole && frameCounter % logFrequency == 0)
        {
            LogHandData(rightHandData);
        }
    }

    #endregion

    /// <summary>
    /// Met à jour les données d'une main à partir des données XRHand
    /// </summary>
    private void UpdateHandData(XRHand hand, HandData handData)
    {
        handData.isTracked = hand.isTracked;

        if (!hand.isTracked) return;

        // Récupérer la pose du poignet (root)
        handData.wristPosition = hand.rootPose.position;
        handData.wristRotation = hand.rootPose.rotation;
        handData.wristEulerAngles = hand.rootPose.rotation.eulerAngles;

        // Récupérer les positions des bouts des doigts
        TryGetJointPosition(hand, XRHandJointID.ThumbTip, out handData.thumbTipPosition);
        TryGetJointPosition(hand, XRHandJointID.IndexTip, out handData.indexTipPosition);
        TryGetJointPosition(hand, XRHandJointID.MiddleTip, out handData.middleTipPosition);
        TryGetJointPosition(hand, XRHandJointID.RingTip, out handData.ringTipPosition);
        TryGetJointPosition(hand, XRHandJointID.LittleTip, out handData.littleTipPosition);

        // Récupérer toutes les poses des joints si demandé
        if (logAllJoints)
        {
            for (int i = 0; i < XRHandJointID.EndMarker.ToIndex(); i++)
            {
                XRHandJointID jointId = XRHandJointIDUtility.FromIndex(i);
                XRHandJoint joint = hand.GetJoint(jointId);
                
                handData.allJoints[i].jointName = jointId.ToString();
                handData.allJoints[i].isValid = joint.TryGetPose(out Pose pose);
                
                if (handData.allJoints[i].isValid)
                {
                    handData.allJoints[i].position = pose.position;
                    handData.allJoints[i].rotation = pose.rotation;
                }
            }
        }
    }

    /// <summary>
    /// Essaie de récupérer la position d'un joint spécifique
    /// </summary>
    private bool TryGetJointPosition(XRHand hand, XRHandJointID jointId, out Vector3 position)
    {
        XRHandJoint joint = hand.GetJoint(jointId);
        if (joint.TryGetPose(out Pose pose))
        {
            position = pose.position;
            return true;
        }
        position = Vector3.zero;
        return false;
    }

    #region Affichage des infos en console
        
    #endregion

    /// <summary>
    /// Affiche les données d'une main dans la console de manière formatée
    /// </summary>
    private void LogHandData(HandData handData)
    {
        if (!handData.isTracked) return;

        stringBuilder.Clear();
        stringBuilder.AppendLine($"<color=yellow>══════════ {handData.handName} ══════════</color>");
        stringBuilder.AppendLine($"  <color=white>Poignet:</color>");
        stringBuilder.AppendLine($"    Position: {FormatVector3(handData.wristPosition)}");
        stringBuilder.AppendLine($"    Rotation: {FormatVector3(handData.wristEulerAngles)}°");
        
        stringBuilder.AppendLine($"  <color=white>Bouts des doigts:</color>");
        stringBuilder.AppendLine($"    Pouce:       {FormatVector3(handData.thumbTipPosition)}");
        stringBuilder.AppendLine($"    Index:       {FormatVector3(handData.indexTipPosition)}");
        stringBuilder.AppendLine($"    Majeur:      {FormatVector3(handData.middleTipPosition)}");
        stringBuilder.AppendLine($"    Annulaire:   {FormatVector3(handData.ringTipPosition)}");
        stringBuilder.AppendLine($"    Auriculaire: {FormatVector3(handData.littleTipPosition)}");

        // Distance pouce-index (pinch)
        float pinchDistance = Vector3.Distance(handData.thumbTipPosition, handData.indexTipPosition);
        stringBuilder.AppendLine($"  <color=magenta>Distance Pinch: {pinchDistance * 100:F1}cm</color>");

        // Afficher tous les joints si demandé
        if (logAllJoints)
        {
            stringBuilder.AppendLine($"  <color=white>Tous les joints:</color>");
            for (int i = 0; i < handData.allJoints.Length; i++)
            {
                var joint = handData.allJoints[i];
                if (joint.isValid)
                {
                    stringBuilder.AppendLine($"    {joint.jointName}: {FormatVector3(joint.position)}");
                }
            }
        }

        Debug.Log(stringBuilder.ToString());
    }

    /// <summary>
    /// Formate un Vector3 pour un affichage lisible
    /// </summary>
    private string FormatVector3(Vector3 v)
    {
        return $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    }
}
