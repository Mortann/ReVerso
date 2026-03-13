using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panneau Thérapie Miroir côté soignant (PC).
/// 
/// FLUX UTILISATEUR :
///   1. Vérifier connexion (IP affichée en haut)
///   2. Cliquer "Lancer le calibrage" → Quest effectue la calibration, renvoie résultat
///   3. Choisir le côté affecté : Gauche ou Droit
///   4. Cliquer "Activer la thérapie miroir"
///   5. Cliquer "Désactiver" pour arrêter
///   6. "Retour" pour revenir au menu principal
///
/// CONFIGURATION UNITY :
///   Créer un Canvas avec un panneau et assigner les références dans l'inspecteur.
///   Le script est déjà abonné aux messages réseau via SoignantClient.
/// </summary>
public class MirrorTherapySessionPanel : MonoBehaviour
{
    // ──────────────────────────────── RÉFÉRENCES ────────────────────────────────

    [Header("Réseau")]
    [SerializeField] private SoignantClient soignantClient;

    [Header("Connexion — haut du panneau")]
    [Tooltip("Texte affiché : 'Connecté : 192.168.x.x' ou 'Non connecté'")]
    [SerializeField] private TextMeshProUGUI txtConnectionStatus;

    [Tooltip("Image / icône circulaire de statut (vert = connecté, rouge = déconnecté)")]
    [SerializeField] private Image imgConnectionDot;

    [SerializeField] private Color colorConnected   = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color colorDisconnected = new Color(0.8f, 0.2f, 0.2f);

    [Header("Calibrage")]
    [SerializeField] private Button btnCalibrate;
    [SerializeField] private TextMeshProUGUI txtCalibrateStatus;

    [Header("Côté affecté")]
    [SerializeField] private Button btnLeft;
    [SerializeField] private Button btnRight;

    [Tooltip("Couleur du bouton côté sélectionné")]
    [SerializeField] private Color colorSelected   = new Color(0.3f, 0.6f, 1f);
    [Tooltip("Couleur des boutons non-sélectionnés")]
    [SerializeField] private Color colorUnselected = new Color(0.25f, 0.25f, 0.25f);

    [Header("Thérapie Miroir")]
    [SerializeField] private Button btnToggleMirror;
    [SerializeField] private TextMeshProUGUI txtToggleMirrorLabel;
    [SerializeField] private TextMeshProUGUI txtMirrorStatus;

    [Header("Retour Vidéo")]
    [Tooltip("RawImage pour afficher le flux vidéo du casque")]
    [SerializeField] private RawImage videoDisplay;

    [Tooltip("Texte affiché quand pas de flux vidéo")]
    [SerializeField] private TextMeshProUGUI txtNoVideo;

    [Header("Navigation")]
    [SerializeField] private Button btnBack;

    // ──────────────────────────────── ÉTAT LOCAL ────────────────────────────────

    private bool isCalibrated       = false;
    private bool isLeftSelected     = true;   // true = gauche, false = droit
    private bool isMirrorActive     = false;
    private bool isConnected        = false;
    private bool streamActive       = false;
    private Texture2D videoTexture;

    // ─────────────────────────────── UNITY LIFECYCLE ────────────────────────────

    private void Start()
    {
        if (soignantClient == null)
            soignantClient = FindFirstObjectByType<SoignantClient>();

        if (soignantClient != null)
        {
            soignantClient.OnConnected    += HandleConnected;
            soignantClient.OnDisconnected += HandleDisconnected;
            soignantClient.OnMessageReceived += HandleNetworkMessage;

            isConnected = soignantClient.IsConnected;
        }

        // Boutons
        if (btnCalibrate    != null) btnCalibrate.onClick.AddListener(OnCalibrateClicked);
        if (btnLeft         != null) btnLeft.onClick.AddListener(OnLeftSelected);
        if (btnRight        != null) btnRight.onClick.AddListener(OnRightSelected);
        if (btnToggleMirror != null) btnToggleMirror.onClick.AddListener(OnToggleMirrorClicked);
        if (btnBack         != null) btnBack.onClick.AddListener(OnBackClicked);

        RefreshAll();
    }

    private void OnDestroy()
    {
        if (soignantClient != null)
        {
            soignantClient.OnConnected       -= HandleConnected;
            soignantClient.OnDisconnected    -= HandleDisconnected;
            soignantClient.OnMessageReceived -= HandleNetworkMessage;
        }

        if (btnCalibrate    != null) btnCalibrate.onClick.RemoveListener(OnCalibrateClicked);
        if (btnLeft         != null) btnLeft.onClick.RemoveListener(OnLeftSelected);
        if (btnRight        != null) btnRight.onClick.RemoveListener(OnRightSelected);
        if (btnToggleMirror != null) btnToggleMirror.onClick.RemoveListener(OnToggleMirrorClicked);
        if (btnBack         != null) btnBack.onClick.RemoveListener(OnBackClicked);

        if (videoTexture != null)
            Destroy(videoTexture);
    }

    private void OnEnable()
    {
        // Rafraîchir le statut connexion à chaque fois que le panneau est affiché
        isConnected = soignantClient != null && soignantClient.IsConnected;
        RefreshAll();

        // Démarrer le streaming vidéo
        if (isConnected)
        {
            soignantClient.SendCommand(NetworkMessageType.StartStreaming);
            streamActive = true;
        }
    }

    private void OnDisable()
    {
        // Arrêter le streaming vidéo
        if (soignantClient != null && soignantClient.IsConnected && streamActive)
        {
            soignantClient.SendCommand(NetworkMessageType.StopStreaming);
            streamActive = false;
        }
    }

    // ──────────────────────────────── EVENTS RÉSEAU ─────────────────────────────

    private void HandleConnected()
    {
        isConnected = true;
        RefreshConnectionStatus();
        RefreshButtons();

        // Démarrer le streaming si le panneau est actif
        if (gameObject.activeInHierarchy)
        {
            soignantClient.SendCommand(NetworkMessageType.StartStreaming);
            streamActive = true;
        }
    }

    private void HandleDisconnected()
    {
        isConnected = false;
        isCalibrated = false;
        isMirrorActive = false;
        streamActive = false;

        // Réinitialiser l'affichage vidéo
        if (videoDisplay != null)
            videoDisplay.texture = null;
        if (txtNoVideo != null)
            txtNoVideo.gameObject.SetActive(true);

        RefreshAll();
    }

    private void HandleNetworkMessage(NetworkMessage msg)
    {
        switch (msg.type)
        {
            case NetworkMessageType.CalibrationResult:
                OnCalibrationResult(msg.data);
                break;

            case NetworkMessageType.MirrorTherapyStatus:
                OnMirrorTherapyStatusReceived(msg.data);
                break;

            case NetworkMessageType.VideoFrame:
                DecodeAndDisplayFrame(msg.data);
                break;

            case NetworkMessageType.StatusUpdate:
                break;
        }
    }

    private void OnCalibrationResult(string result)
    {
        switch (result)
        {
            case "ok":
                isCalibrated = true;
                if (txtCalibrateStatus != null)
                {
                    txtCalibrateStatus.text = "✓ Calibrage réussi";
                    txtCalibrateStatus.color = colorConnected;
                }
                break;

            case "already_calibrated":
                isCalibrated = true;
                if (txtCalibrateStatus != null)
                {
                    txtCalibrateStatus.text = "✓ Déjà calibré";
                    txtCalibrateStatus.color = colorConnected;
                }
                break;

            default: // "failed"
                isCalibrated = false;
                if (txtCalibrateStatus != null)
                {
                    txtCalibrateStatus.text = "✗ Calibrage échoué — retentez";
                    txtCalibrateStatus.color = colorDisconnected;
                }
                break;
        }

        RefreshButtons();
    }

    private void OnMirrorTherapyStatusReceived(string status)
    {
        isMirrorActive = (status == "active");
        RefreshMirrorButton();
    }

    // ──────────────────────────────── BOUTONS ───────────────────────────────────

    private void OnCalibrateClicked()
    {
        if (!isConnected) return;

        if (txtCalibrateStatus != null)
        {
            txtCalibrateStatus.text = "Calibrage en cours…";
            txtCalibrateStatus.color = Color.yellow;
        }

        soignantClient.SendCommand(NetworkMessageType.Calibrate);
    }

    private void OnLeftSelected()
    {
        isLeftSelected = true;
        RefreshSideButtons();
        if (isConnected)
            soignantClient.SendCommand(NetworkMessageType.SetAffectedSide, "left");
    }

    private void OnRightSelected()
    {
        isLeftSelected = false;
        RefreshSideButtons();
        if (isConnected)
            soignantClient.SendCommand(NetworkMessageType.SetAffectedSide, "right");
    }

    private void OnToggleMirrorClicked()
    {
        if (!isConnected || !isCalibrated) return;

        if (isMirrorActive)
            soignantClient.SendCommand(NetworkMessageType.DeactivateMirrorTherapy);
        else
            soignantClient.SendCommand(NetworkMessageType.ActivateMirrorTherapy);

        // Mise à jour optimiste (la vraie confirmation arrive via MirrorTherapyStatus)
        isMirrorActive = !isMirrorActive;
        RefreshMirrorButton();
    }

    private void OnBackClicked()
    {
        // Si la thérapie miroir est active, on la désactive proprement avant de quitter
        if (isMirrorActive && isConnected)
        {
            soignantClient.SendCommand(NetworkMessageType.DeactivateMirrorTherapy);
            isMirrorActive = false;
        }

        // Retour au menu principal via SoignantUIManager
        var uiManager = FindFirstObjectByType<SoignantUIManager>();
        if (uiManager != null)
            uiManager.ShowMainMenu();
    }

    // ──────────────────────────────── RETOUR VIDÉO ─────────────────────────────

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
            Debug.LogWarning($"[MirrorTherapySessionPanel] Erreur décodage frame: {e.Message}");
        }
    }

    // ──────────────────────────────── REFRESH UI ────────────────────────────────

    private void RefreshAll()
    {
        RefreshConnectionStatus();
        RefreshButtons();
        RefreshSideButtons();
        RefreshMirrorButton();
        RefreshCalibrateStatus();
    }

    private void RefreshConnectionStatus()
    {
        if (txtConnectionStatus != null)
        {
            if (isConnected)
            {
                string ip = soignantClient != null ? soignantClient.ConnectedHeadsetIP : "?";
                txtConnectionStatus.text = $"Connecté : {ip}";
            }
            else
            {
                txtConnectionStatus.text = "Non connecté";
            }
        }

        if (imgConnectionDot != null)
            imgConnectionDot.color = isConnected ? colorConnected : colorDisconnected;
    }

    private void RefreshButtons()
    {
        // Le calibrage et la sélection de côté nécessitent une connexion
        if (btnCalibrate != null)
            btnCalibrate.interactable = isConnected;

        if (btnLeft != null)
            btnLeft.interactable = isConnected;

        if (btnRight != null)
            btnRight.interactable = isConnected;

        // Le démarrage de la thérapie miroir nécessite connexion + calibrage
        if (btnToggleMirror != null)
            btnToggleMirror.interactable = isConnected && isCalibrated;
    }

    private void RefreshCalibrateStatus()
    {
        if (txtCalibrateStatus != null && !isCalibrated)
        {
            txtCalibrateStatus.text = isConnected ? "En attente du calibrage" : "Casque non connecté";
            txtCalibrateStatus.color = Color.gray;
        }
    }

    private void RefreshSideButtons()
    {
        SetButtonColor(btnLeft,  isLeftSelected  ? colorSelected : colorUnselected);
        SetButtonColor(btnRight, !isLeftSelected ? colorSelected : colorUnselected);
    }

    private void RefreshMirrorButton()
    {
        if (txtToggleMirrorLabel != null)
            txtToggleMirrorLabel.text = isMirrorActive
                ? "Désactiver la thérapie miroir"
                : "Activer la thérapie miroir";

        if (txtMirrorStatus != null)
        {
            txtMirrorStatus.text  = isMirrorActive ? "Thérapie miroir ACTIVE" : "Thérapie miroir inactive";
            txtMirrorStatus.color = isMirrorActive ? colorConnected            : Color.gray;
        }
    }

    private static void SetButtonColor(Button btn, Color color)
    {
        if (btn == null) return;
        var colors = btn.colors;
        colors.normalColor = color;
        btn.colors = colors;
    }
}
