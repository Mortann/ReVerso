using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panneau côté soignant (PC) qui affiche :
/// - Le flux vidéo du casque Quest (ce que le patient voit)
/// - Les paramètres et contrôles de la séance
/// 
/// SETUP DANS LA SCÈNE :
/// 1. Créer un Canvas avec ce script
/// 2. Assigner les références UI (RawImage pour la vidéo, boutons, textes)
/// 3. Le script s'abonne aux messages réseau du SoignantClient
///
/// LAYOUT (split horizontal) :
///   ┌─────────────────────┬──────────────────┐
///   │                     │  PARAMÈTRES       │
///   │   VUE DU CASQUE     │  - Statut         │
///   │   (flux vidéo)      │  - Exercice       │
///   │                     │  - Passthrough     │
///   │                     │  - Tracking data   │
///   └─────────────────────┴──────────────────┘
/// </summary>
public class HeadsetViewPanel : MonoBehaviour
{
    [Header("Réseau")]
    [SerializeField] private SoignantClient soignantClient;

    [Header("Flux Vidéo")]
    [Tooltip("RawImage pour afficher le flux vidéo du casque")]
    [SerializeField] private RawImage videoDisplay;

    [Tooltip("Texte affiché quand pas de flux vidéo")]
    [SerializeField] private TextMeshProUGUI txtNoVideo;

    [Header("Paramètres Séance")]
    [Tooltip("Texte du statut de connexion")]
    [SerializeField] private TextMeshProUGUI txtConnectionStatus;

    [Tooltip("Texte du statut du casque")]
    [SerializeField] private TextMeshProUGUI txtHeadsetStatus;

    [Tooltip("Texte des données de tracking (amplitude mains)")]
    [SerializeField] private TextMeshProUGUI txtTrackingData;

    [Tooltip("Texte des métriques de la dernière séance")]
    [SerializeField] private TextMeshProUGUI txtMetrics;

    [Header("Boutons Contrôle Séance")]
    [Tooltip("Bouton Démarrer / Arrêter exercice")]
    [SerializeField] private Button btnToggleExercise;
    [SerializeField] private TextMeshProUGUI btnToggleExerciseText;

    [Tooltip("Bouton Toggle Passthrough")]
    [SerializeField] private Button btnTogglePassthrough;
    [SerializeField] private TextMeshProUGUI btnTogglePassthroughText;

    [Tooltip("Bouton Retour au menu principal")]
    [SerializeField] private Button btnRetour;

    [Header("État")]
    [SerializeField] private bool isExerciseRunning = false;
    [SerializeField] private bool isPassthroughActive = false;

    private Texture2D videoTexture;
    private bool streamActive = false;

    // Tracking data display
    private float leftAmplitude = 0f;
    private float rightAmplitude = 0f;
    private bool leftTracked = false;
    private bool rightTracked = false;
    private string lastHeadsetStatus = "En attente...";

    private void Start()
    {
        if (soignantClient == null)
            soignantClient = FindFirstObjectByType<SoignantClient>();

        if (soignantClient != null)
        {
            soignantClient.OnMessageReceived += OnNetworkMessage;
            soignantClient.OnConnected += OnConnected;
            soignantClient.OnDisconnected += OnDisconnected;
        }

        // Configurer les boutons
        if (btnToggleExercise != null)
        {
            btnToggleExercise.onClick.AddListener(ToggleExercise);
            btnToggleExercise.interactable = false;
        }

        if (btnTogglePassthrough != null)
        {
            btnTogglePassthrough.onClick.AddListener(TogglePassthrough);
            btnTogglePassthrough.interactable = false;
        }

        UpdateUI();
    }

    private void OnDestroy()
    {
        if (soignantClient != null && soignantClient.IsConnected && streamActive)
        {
            soignantClient.SendCommand(NetworkMessageType.StopStreaming);
            streamActive = false;
        }

        if (soignantClient != null)
        {
            soignantClient.OnMessageReceived -= OnNetworkMessage;
            soignantClient.OnConnected -= OnConnected;
            soignantClient.OnDisconnected -= OnDisconnected;
        }

        if (btnToggleExercise != null)
            btnToggleExercise.onClick.RemoveListener(ToggleExercise);
        if (btnTogglePassthrough != null)
            btnTogglePassthrough.onClick.RemoveListener(TogglePassthrough);

        if (videoTexture != null)
            Destroy(videoTexture);
    }

    private void OnEnable()
    {
        // Demander le streaming au casque quand on ouvre ce panneau
        if (soignantClient != null && soignantClient.IsConnected)
        {
            soignantClient.SendCommand(NetworkMessageType.StartStreaming);
            streamActive = true;
        }
    }

    private void OnDisable()
    {
        // Arrêter le streaming quand on ferme ce panneau
        if (soignantClient != null && soignantClient.IsConnected && streamActive)
        {
            soignantClient.SendCommand(NetworkMessageType.StopStreaming);
            streamActive = false;
        }
    }

    // ═══════════════════════════════════════════════════
    #region Network Events
    // ═══════════════════════════════════════════════════

    private void OnConnected()
    {
        if (btnToggleExercise != null)
            btnToggleExercise.interactable = true;
        if (btnTogglePassthrough != null)
            btnTogglePassthrough.interactable = true;

        // Démarrer le streaming si le panneau est actif
        if (gameObject.activeInHierarchy)
        {
            soignantClient.SendCommand(NetworkMessageType.StartStreaming);
            streamActive = true;
        }

        UpdateUI();
    }

    private void OnDisconnected()
    {
        if (btnToggleExercise != null)
            btnToggleExercise.interactable = false;
        if (btnTogglePassthrough != null)
            btnTogglePassthrough.interactable = false;

        streamActive = false;
        isExerciseRunning = false;
        isPassthroughActive = false;
        leftTracked = false;
        rightTracked = false;
        lastHeadsetStatus = "Déconnecté";

        // Afficher le message "pas de vidéo"
        if (videoDisplay != null)
            videoDisplay.texture = null;
        if (txtNoVideo != null)
            txtNoVideo.gameObject.SetActive(true);

        UpdateUI();
    }

    private void OnNetworkMessage(NetworkMessage message)
    {
        switch (message.type)
        {
            case NetworkMessageType.VideoFrame:
                DecodeAndDisplayFrame(message.data);
                break;

            case NetworkMessageType.HandTrackingData:
                ProcessTrackingData(message.data);
                break;

            case NetworkMessageType.StatusUpdate:
                lastHeadsetStatus = message.data;
                UpdateUI();
                break;

            case NetworkMessageType.ExerciseCompleted:
                OnExerciseCompleted(message.data);
                break;
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════
    #region Video Display
    // ═══════════════════════════════════════════════════

    private void DecodeAndDisplayFrame(string base64Data)
    {
        if (string.IsNullOrEmpty(base64Data)) return;
        if (videoDisplay == null) return;

        try
        {
            byte[] jpegData = Convert.FromBase64String(base64Data);

            if (videoTexture == null)
                videoTexture = new Texture2D(2, 2);

            ImageConversion.LoadImage(videoTexture, jpegData);
            videoDisplay.texture = videoTexture;

            if (txtNoVideo != null)
                txtNoVideo.gameObject.SetActive(false);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HeadsetViewPanel] Erreur décodage frame: {e.Message}");
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════
    #region Tracking Data
    // ═══════════════════════════════════════════════════

    private void ProcessTrackingData(string jsonData)
    {
        try
        {
            HandAmplitudeData data = JsonUtility.FromJson<HandAmplitudeData>(jsonData);
            if (data.hand == "left")
            {
                leftAmplitude = data.amplitude;
                leftTracked = data.isTracked;
            }
            else if (data.hand == "right")
            {
                rightAmplitude = data.amplitude;
                rightTracked = data.isTracked;
            }
            UpdateTrackingUI();
        }
        catch { }
    }

    private void OnExerciseCompleted(string metriquesJson)
    {
        isExerciseRunning = false;

        if (txtMetrics != null && !string.IsNullOrEmpty(metriquesJson))
        {
            txtMetrics.text = $"Exercice terminé\nDonnées reçues";
        }

        UpdateUI();
    }

    #endregion

    // ═══════════════════════════════════════════════════
    #region Actions
    // ═══════════════════════════════════════════════════

    private void ToggleExercise()
    {
        if (soignantClient == null || !soignantClient.IsConnected) return;

        isExerciseRunning = !isExerciseRunning;

        soignantClient.SendCommand(isExerciseRunning
            ? NetworkMessageType.StartExercise
            : NetworkMessageType.StopExercise);

        UpdateUI();
    }

    private void TogglePassthrough()
    {
        if (soignantClient == null || !soignantClient.IsConnected) return;

        isPassthroughActive = !isPassthroughActive;

        soignantClient.SendCommand(isPassthroughActive
            ? NetworkMessageType.EnablePassthrough
            : NetworkMessageType.DisablePassthrough);

        UpdateUI();
    }

    #endregion

    // ═══════════════════════════════════════════════════
    #region UI Updates
    // ═══════════════════════════════════════════════════

    private void UpdateUI()
    {
        bool connected = soignantClient != null && soignantClient.IsConnected;

        if (txtConnectionStatus != null)
        {
            txtConnectionStatus.text = connected
                ? $"Connecté : {soignantClient.ConnectedHeadsetIP}"
                : "Non connecté";
            txtConnectionStatus.color = connected ? Color.green : Color.red;
        }

        if (txtHeadsetStatus != null)
            txtHeadsetStatus.text = $"Casque : {lastHeadsetStatus}";

        if (btnToggleExerciseText != null)
            btnToggleExerciseText.text = isExerciseRunning ? "Arrêter l'exercice" : "Démarrer l'exercice";

        if (btnTogglePassthroughText != null)
            btnTogglePassthroughText.text = isPassthroughActive ? "Reprendre la VR" : "Pause VR (Passthrough)";

        UpdateTrackingUI();
    }

    private void UpdateTrackingUI()
    {
        if (txtTrackingData == null) return;

        string leftStatus = leftTracked ? $"{leftAmplitude:P0}" : "non trackée";
        string rightStatus = rightTracked ? $"{rightAmplitude:P0}" : "non trackée";

        txtTrackingData.text = $"Main gauche : {leftStatus}\nMain droite : {rightStatus}";
    }

    #endregion
}
