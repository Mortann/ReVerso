using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>
/// Script de débogage pour visualiser les données de tracking des mains.
/// Peut fonctionner de deux manières :
/// 1. Avec des références directes à XRHandTrackingEvents (si disponibles)
/// 2. En accédant directement au XRHandSubsystem (mode automatique)
/// </summary>
public class HandTrackingDebugger : MonoBehaviour
{
    [Header("Mode de fonctionnement")]
    [Tooltip("Si activé, cherche automatiquement le XRHandSubsystem au lieu d'utiliser les références")]
    [SerializeField] private bool useAutoDetection = true;

    [Header("Références manuelles (si useAutoDetection = false)")]
    [Tooltip("Référence au composant XRHandTrackingEvents pour la main gauche")]
    [SerializeField] private XRHandTrackingEvents leftHandTrackingEvents;
    
    [Tooltip("Référence au composant XRHandTrackingEvents pour la main droite")]
    [SerializeField] private XRHandTrackingEvents rightHandTrackingEvents;

    [Header("Options de débogage")]
    [Tooltip("Afficher les logs dans la console Unity")]
    [SerializeField] private bool logToConsole = true;
    
    [Tooltip("Fréquence des logs (1 = chaque frame, 30 = toutes les 30 frames)")]
    [SerializeField] private int logFrequency = 30;
    
    [Tooltip("Afficher uniquement les joints principaux (poignet + bouts des doigts)")]
    [SerializeField] private bool showOnlyKeyJoints = true;
    
    [Tooltip("Afficher tous les 26 joints dans la console (verbose)")]
    [SerializeField] private bool logAllJoints = false;

    [Header("UI Debug")]
    [Tooltip("Afficher l'UI de débogage à l'écran")]
    [SerializeField] private bool showDebugUI = true;
    
    [Tooltip("Position de l'UI de débogage")]
    [SerializeField] private Vector2 uiPosition = new Vector2(10, 10);
    
    [Tooltip("Taille de la police")]
    [SerializeField] private int fontSize = 14;

    // Données actuelles des mains
    private HandData leftHandData = new HandData("Main Gauche");
    private HandData rightHandData = new HandData("Main Droite");
    
    // Pour le mode auto-détection
    private XRHandSubsystem handSubsystem;
    private static readonly List<XRHandSubsystem> subsystemsReuse = new List<XRHandSubsystem>();
    private bool wasLeftTracked = false;
    private bool wasRightTracked = false;
    
    private int frameCounter = 0;
    private GUIStyle guiStyle;
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
        
        // Positions des bouts des doigts (les plus utiles pour débuguer)
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
        if (useAutoDetection)
        {
            Debug.Log("<color=green>[HandTrackingDebugger]</color> Mode auto-détection activé. Recherche du XRHandSubsystem...");
            // Le subsystem sera trouvé dans Update()
        }
        else
        {
            SubscribeToTrackingEvents();
        }
    }

    private void OnDisable()
    {
        if (useAutoDetection)
        {
            UnsubscribeFromSubsystem();
        }
        else
        {
            UnsubscribeFromTrackingEvents();
        }
    }

    private void Update()
    {
        frameCounter++;

        if (useAutoDetection)
        {
            // Chercher le subsystem s'il n'est pas encore trouvé ou s'il ne fonctionne plus
            if (handSubsystem == null || !handSubsystem.running)
            {
                TryFindHandSubsystem();
            }

            // Mettre à jour les données des mains directement depuis le subsystem
            if (handSubsystem != null && handSubsystem.running)
            {
                UpdateHandDataFromSubsystem(handSubsystem.leftHand, leftHandData, ref wasLeftTracked, "GAUCHE");
                UpdateHandDataFromSubsystem(handSubsystem.rightHand, rightHandData, ref wasRightTracked, "DROITE");

                // Logger périodiquement
                if (logToConsole && frameCounter % logFrequency == 0)
                {
                    if (leftHandData.isTracked) LogHandData(leftHandData);
                    if (rightHandData.isTracked) LogHandData(rightHandData);
                }
            }
        }
    }

    #region Auto-Detection Mode

    private void TryFindHandSubsystem()
    {
        SubsystemManager.GetSubsystems(subsystemsReuse);
        
        for (int i = 0; i < subsystemsReuse.Count; i++)
        {
            if (subsystemsReuse[i].running)
            {
                handSubsystem = subsystemsReuse[i];
                SubscribeToSubsystem();
                Debug.Log("<color=green>[HandTrackingDebugger]</color> ✓ XRHandSubsystem trouvé et actif !");
                return;
            }
        }
    }

    private void SubscribeToSubsystem()
    {
        if (handSubsystem != null)
        {
            handSubsystem.updatedHands += OnSubsystemUpdatedHands;
            handSubsystem.trackingAcquired += OnSubsystemTrackingAcquired;
            handSubsystem.trackingLost += OnSubsystemTrackingLost;
        }
    }

    private void UnsubscribeFromSubsystem()
    {
        if (handSubsystem != null)
        {
            handSubsystem.updatedHands -= OnSubsystemUpdatedHands;
            handSubsystem.trackingAcquired -= OnSubsystemTrackingAcquired;
            handSubsystem.trackingLost -= OnSubsystemTrackingLost;
            handSubsystem = null;
        }
    }

    private void OnSubsystemTrackingAcquired(XRHand hand)
    {
        string handName = hand.handedness == Handedness.Left ? "GAUCHE" : "DROITE";
        Debug.Log($"<color=cyan>[HandTrackingDebugger]</color> 👋 Main {handName} détectée !");
    }

    private void OnSubsystemTrackingLost(XRHand hand)
    {
        string handName = hand.handedness == Handedness.Left ? "GAUCHE" : "DROITE";
        Debug.Log($"<color=orange>[HandTrackingDebugger]</color> ✋ Main {handName} perdue !");
        
        if (hand.handedness == Handedness.Left)
            leftHandData.isTracked = false;
        else
            rightHandData.isTracked = false;
    }

    private void OnSubsystemUpdatedHands(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType updateType)
    {
        // On utilise déjà Update() pour lire les données, donc on n'a rien à faire ici
        // Mais cet événement confirme que le système fonctionne
    }

    private void UpdateHandDataFromSubsystem(XRHand hand, HandData handData, ref bool wasTracked, string handName)
    {
        handData.isTracked = hand.isTracked;

        // Détecter les changements de tracking
        if (hand.isTracked != wasTracked)
        {
            if (hand.isTracked)
                Debug.Log($"<color=cyan>[HandTrackingDebugger]</color> 👋 Main {handName} détectée !");
            else
                Debug.Log($"<color=orange>[HandTrackingDebugger]</color> ✋ Main {handName} perdue !");
            wasTracked = hand.isTracked;
        }

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

        // Récupérer toutes les poses des joints pour un debug complet
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

    #endregion

    #region Manual Mode - XRHandTrackingEvents

    private void SubscribeToTrackingEvents()
    {
        // S'abonner aux événements de la main gauche
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.AddListener(OnLeftHandJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.AddListener(OnLeftHandTrackingAcquired);
            leftHandTrackingEvents.trackingLost.AddListener(OnLeftHandTrackingLost);
            Debug.Log("<color=green>[HandTrackingDebugger]</color> Abonné aux événements de la main gauche");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[HandTrackingDebugger]</color> Pas de XRHandTrackingEvents assigné pour la main gauche !");
        }

        // S'abonner aux événements de la main droite
        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.AddListener(OnRightHandJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.AddListener(OnRightHandTrackingAcquired);
            rightHandTrackingEvents.trackingLost.AddListener(OnRightHandTrackingLost);
            Debug.Log("<color=green>[HandTrackingDebugger]</color> Abonné aux événements de la main droite");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[HandTrackingDebugger]</color> Pas de XRHandTrackingEvents assigné pour la main droite !");
        }
    }

    private void UnsubscribeFromTrackingEvents()
    {
        // Se désabonner des événements
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.RemoveListener(OnLeftHandJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.RemoveListener(OnLeftHandTrackingAcquired);
            leftHandTrackingEvents.trackingLost.RemoveListener(OnLeftHandTrackingLost);
        }

        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.RemoveListener(OnRightHandJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.RemoveListener(OnRightHandTrackingAcquired);
            rightHandTrackingEvents.trackingLost.RemoveListener(OnRightHandTrackingLost);
        }
    }

    #endregion

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
        UpdateHandData(args, leftHandData);
        
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
        UpdateHandData(args, rightHandData);
        
        if (logToConsole && frameCounter % logFrequency == 0)
        {
            LogHandData(rightHandData);
        }
    }

    #endregion

    /// <summary>
    /// Met à jour les données d'une main à partir des événements de tracking (mode manuel)
    /// </summary>
    private void UpdateHandData(XRHandJointsUpdatedEventArgs args, HandData handData)
    {
        XRHand hand = args.hand;
        handData.isTracked = hand.isTracked;

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

        // Récupérer toutes les poses des joints pour un debug complet
        if (!showOnlyKeyJoints)
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

    /// <summary>
    /// Affiche les données d'une main dans la console de manière formatée
    /// </summary>
    private void LogHandData(HandData handData)
    {
        stringBuilder.Clear();
        stringBuilder.AppendLine($"<color=yellow>══════════ {handData.handName} ══════════</color>");
        stringBuilder.AppendLine($"  Trackée: {(handData.isTracked ? "<color=green>OUI</color>" : "<color=red>NON</color>")}");
        
        if (handData.isTracked)
        {
            stringBuilder.AppendLine($"  <color=white>Poignet:</color>");
            stringBuilder.AppendLine($"    Position: {FormatVector3(handData.wristPosition)}");
            stringBuilder.AppendLine($"    Rotation: {FormatVector3(handData.wristEulerAngles)}°");
            
            stringBuilder.AppendLine($"  <color=white>Bouts des doigts:</color>");
            stringBuilder.AppendLine($"    Pouce:      {FormatVector3(handData.thumbTipPosition)}");
            stringBuilder.AppendLine($"    Index:      {FormatVector3(handData.indexTipPosition)}");
            stringBuilder.AppendLine($"    Majeur:     {FormatVector3(handData.middleTipPosition)}");
            stringBuilder.AppendLine($"    Annulaire:  {FormatVector3(handData.ringTipPosition)}");
            stringBuilder.AppendLine($"    Auriculaire:{FormatVector3(handData.littleTipPosition)}");

            // Calculer la distance pouce-index (utile pour détecter un pinch)
            float pinchDistance = Vector3.Distance(handData.thumbTipPosition, handData.indexTipPosition);
            stringBuilder.AppendLine($"  <color=magenta>Distance Pinch (pouce-index): {pinchDistance:F3}m</color>");
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

    #region Debug UI (OnGUI)

    private void OnGUI()
    {
        if (!showDebugUI) return;

        // Initialiser le style si nécessaire
        if (guiStyle == null)
        {
            guiStyle = new GUIStyle(GUI.skin.box);
            guiStyle.fontSize = fontSize;
            guiStyle.alignment = TextAnchor.UpperLeft;
            guiStyle.normal.textColor = Color.white;
            guiStyle.richText = true;
        }

        float panelWidth = 350;
        float panelHeight = 300;
        float spacing = 10;

        // Panel Main Gauche
        Rect leftRect = new Rect(uiPosition.x, uiPosition.y, panelWidth, panelHeight);
        GUI.Box(leftRect, BuildUIText(leftHandData), guiStyle);

        // Panel Main Droite
        Rect rightRect = new Rect(uiPosition.x + panelWidth + spacing, uiPosition.y, panelWidth, panelHeight);
        GUI.Box(rightRect, BuildUIText(rightHandData), guiStyle);

        // Panel d'instructions
        Rect infoRect = new Rect(uiPosition.x, uiPosition.y + panelHeight + spacing, panelWidth * 2 + spacing, 60);
        GUI.Box(infoRect, BuildInstructionsText(), guiStyle);
    }

    private string BuildUIText(HandData handData)
    {
        stringBuilder.Clear();
        stringBuilder.AppendLine($"<b><size={fontSize + 4}>{handData.handName}</size></b>");
        stringBuilder.AppendLine($"Status: {(handData.isTracked ? "<color=lime>TRACKING</color>" : "<color=red>PERDUE</color>")}");
        stringBuilder.AppendLine();

        if (handData.isTracked)
        {
            stringBuilder.AppendLine("<b>Poignet (Root):</b>");
            stringBuilder.AppendLine($"  Pos: {FormatVector3(handData.wristPosition)}");
            stringBuilder.AppendLine($"  Rot: {FormatVector3(handData.wristEulerAngles)}°");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("<b>Bouts des doigts:</b>");
            stringBuilder.AppendLine($"  Pouce: {FormatVector3(handData.thumbTipPosition)}");
            stringBuilder.AppendLine($"  Index: {FormatVector3(handData.indexTipPosition)}");
            stringBuilder.AppendLine($"  Majeur: {FormatVector3(handData.middleTipPosition)}");
            stringBuilder.AppendLine($"  Annulaire: {FormatVector3(handData.ringTipPosition)}");
            stringBuilder.AppendLine($"  Auriculaire: {FormatVector3(handData.littleTipPosition)}");
            stringBuilder.AppendLine();
            
            float pinchDist = Vector3.Distance(handData.thumbTipPosition, handData.indexTipPosition);
            string pinchColor = pinchDist < 0.02f ? "lime" : (pinchDist < 0.05f ? "yellow" : "white");
            stringBuilder.AppendLine($"<color={pinchColor}>Pinch: {pinchDist * 100:F1}cm</color>");
        }
        else
        {
            stringBuilder.AppendLine("<color=grey>Placez votre main devant");
            stringBuilder.AppendLine("le casque pour la détecter</color>");
        }

        return stringBuilder.ToString();
    }

    private string BuildInstructionsText()
    {
        return "<b>HandTrackingDebugger</b> - Debug en temps réel\n" +
               $"Frame: {frameCounter} | Log Console: {(logToConsole ? "ON" : "OFF")} (toutes les {logFrequency} frames)";
    }

    #endregion
}
