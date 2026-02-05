using UnityEngine;
using TMPro;

/// <summary>
/// Affiche les données de tracking des mains dans une UI personnalisée.
/// Assigne les références Text/TextMeshPro dans l'inspecteur.
/// Nécessite une référence vers le HandTrackingDebugger.
/// </summary>
public class HandTrackingDebuggerUI : MonoBehaviour
{
    [Header("Référence au Debugger")]
    [Tooltip("Référence au script HandTrackingDebugger qui contient les données")]
    [SerializeField] private HandTrackingDebugger handTrackingDebugger;

    [Header("UI Main Gauche")]
    [SerializeField] private TextMeshProUGUI leftHandStatusText;
    [SerializeField] private TextMeshProUGUI leftHandWristPosText;
    [SerializeField] private TextMeshProUGUI leftHandWristRotText;
    [SerializeField] private TextMeshProUGUI leftHandFingersText;
    [SerializeField] private TextMeshProUGUI leftHandPinchText;

    [Header("UI Main Droite")]
    [SerializeField] private TextMeshProUGUI rightHandStatusText;
    [SerializeField] private TextMeshProUGUI rightHandWristPosText;
    [SerializeField] private TextMeshProUGUI rightHandWristRotText;
    [SerializeField] private TextMeshProUGUI rightHandFingersText;
    [SerializeField] private TextMeshProUGUI rightHandPinchText;

    [Header("Options")]
    [Tooltip("Fréquence de mise à jour de l'UI (en frames)")]
    [SerializeField] private int updateFrequency = 5;

    private int frameCounter = 0;

    private void Update()
    {
        frameCounter++;
        
        if (frameCounter % updateFrequency != 0) return;
        if (handTrackingDebugger == null) return;

        // Mettre à jour l'UI de la main gauche
        UpdateHandUI(
            handTrackingDebugger.LeftHandData,
            leftHandStatusText,
            leftHandWristPosText,
            leftHandWristRotText,
            leftHandFingersText,
            leftHandPinchText
        );

        // Mettre à jour l'UI de la main droite
        UpdateHandUI(
            handTrackingDebugger.RightHandData,
            rightHandStatusText,
            rightHandWristPosText,
            rightHandWristRotText,
            rightHandFingersText,
            rightHandPinchText
        );
    }

    private void UpdateHandUI(
        HandTrackingDebugger.HandData handData,
        TextMeshProUGUI statusText,
        TextMeshProUGUI wristPosText,
        TextMeshProUGUI wristRotText,
        TextMeshProUGUI fingersText,
        TextMeshProUGUI pinchText)
    {
        if (handData == null) return;

        // Status
        if (statusText != null)
        {
            statusText.text = handData.isTracked ? "TRACKING" : "PERDUE";
            statusText.color = handData.isTracked ? Color.green : Color.red;
        }

        if (!handData.isTracked)
        {
            // Effacer les autres champs si pas de tracking
            if (wristPosText != null) wristPosText.text = "---";
            if (wristRotText != null) wristRotText.text = "---";
            if (fingersText != null) fingersText.text = "---";
            if (pinchText != null) pinchText.text = "---";
            return;
        }

        // Position du poignet
        if (wristPosText != null)
        {
            wristPosText.text = FormatVector3(handData.wristPosition);
        }

        // Rotation du poignet
        if (wristRotText != null)
        {
            wristRotText.text = FormatVector3(handData.wristEulerAngles) + "°";
        }

        // Bouts des doigts
        if (fingersText != null)
        {
            fingersText.text = $"Pouce: {FormatVector3(handData.thumbTipPosition)}\n" +
                              $"Index: {FormatVector3(handData.indexTipPosition)}\n" +
                              $"Majeur: {FormatVector3(handData.middleTipPosition)}\n" +
                              $"Annulaire: {FormatVector3(handData.ringTipPosition)}\n" +
                              $"Auriculaire: {FormatVector3(handData.littleTipPosition)}";
        }

        // Distance Pinch
        if (pinchText != null)
        {
            float pinchDist = Vector3.Distance(handData.thumbTipPosition, handData.indexTipPosition);
            pinchText.text = $"{pinchDist * 100:F1} cm";
            
            // Couleur selon la distance
            if (pinchDist < 0.02f)
                pinchText.color = Color.green;
            else if (pinchDist < 0.05f)
                pinchText.color = Color.yellow;
            else
                pinchText.color = Color.white;
        }
    }

    private string FormatVector3(Vector3 v)
    {
        return $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    }
}
