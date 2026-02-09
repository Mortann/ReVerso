using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gestionnaire de l'interface utilisateur côté soignant.
/// Gère la navigation entre les différents panneaux et les actions globales.
/// 
/// MODE DE FONCTIONNEMENT:
/// - Si SoignantClient est présent: envoie les commandes via réseau au Quest
/// - Sinon (Quest Link): contrôle directement le PassthroughController local
/// </summary>
public class SoignantUIManager : MonoBehaviour
{
    [Header("Panneaux UI")]
    [Tooltip("Le panneau du menu principal")]
    [SerializeField] private GameObject mainMenuPanel;
    
    [Tooltip("Le panneau Statut Casque (infos de debug)")]
    [SerializeField] private GameObject statutCasquePanel;

    [Tooltip("Le panneau Connexion Casque")]
    [SerializeField] private GameObject connexionCasquePanel;

    [Header("Boutons Navigation")]
    [Tooltip("Bouton pour afficher le statut du casque")]
    [SerializeField] private Button btnStatutCasque;

    [Tooltip("Texte du bouton Statut Casque / Connexion Casque")]
    [SerializeField] private TMPro.TextMeshProUGUI btnStatutCasqueText;
    
    [Tooltip("Bouton retour (menu Statut Casque)")]
    [SerializeField] private Button btnRetourMenuStatut;

    [Tooltip("Bouton retour (menu Connexion Casque)")]
    [SerializeField] private Button btnRetourMenuConnexion;

    [Header("Boutons Actions")]
    [Tooltip("Bouton pour activer/désactiver le passthrough (pause VR)")]
    [SerializeField] private Button btnPauseVR;
    
    [Tooltip("Texte du bouton pause VR (pour changer le libellé)")]
    [SerializeField] private TMPro.TextMeshProUGUI btnPauseVRText;

    [Header("Références - Mode Local (Quest Link)")]
    [Tooltip("Référence au contrôleur de Passthrough (uniquement si même scène/Quest Link)")]
    [SerializeField] private PassthroughController passthroughController;

    [Header("Références - Mode Réseau (Build séparé)")]
    [Tooltip("Référence au client réseau (si builds PC/Quest séparés)")]
    [SerializeField] private SoignantClient soignantClient;

    private bool isPassthroughActive = false;
    private bool useNetworkMode = false;

    private void Start()
    {
        DetectMode();
        
        if (soignantClient != null)
        {
            soignantClient.OnConnected += OnHeadsetConnected;
            soignantClient.OnDisconnected += OnHeadsetDisconnected;
        }
        
        if (btnStatutCasque != null)
            btnStatutCasque.onClick.AddListener(ShowStatutCasque);
        
        if (btnRetourMenuStatut != null)
            btnRetourMenuStatut.onClick.AddListener(ShowMainMenu);

        if (btnRetourMenuConnexion != null)
            btnRetourMenuConnexion.onClick.AddListener(ShowMainMenu);
        
        if (btnPauseVR != null)
            btnPauseVR.onClick.AddListener(TogglePauseVR);

        ShowMainMenu();
        UpdatePauseVRButtonText();
        UpdateStatutCasqueButtonText();
    }

    private void DetectMode()
    {
        if (soignantClient == null)
            soignantClient = FindFirstObjectByType<SoignantClient>();
        
        if (passthroughController == null)
            passthroughController = FindFirstObjectByType<PassthroughController>();
        
        if (soignantClient != null)
        {
            useNetworkMode = true;
            Debug.Log("[SoignantUIManager] Mode RÉSEAU activé (PC → Quest via TCP)");
        }
        else if (passthroughController != null)
        {
            useNetworkMode = false;
            Debug.Log("[SoignantUIManager] Mode LOCAL activé (Quest Link / même scène)");
        }
        else
        {
            Debug.LogWarning("[SoignantUIManager] Aucun mode détecté ! Assignez SoignantClient (réseau) ou PassthroughController (local)");
        }
    }

    private void OnDestroy()
    {
        if (btnStatutCasque != null)
            btnStatutCasque.onClick.RemoveListener(ShowStatutCasque);
        
        if (btnRetourMenuStatut != null)
            btnRetourMenuStatut.onClick.RemoveListener(ShowMainMenu);

        if (btnRetourMenuConnexion != null)
            btnRetourMenuConnexion.onClick.RemoveListener(ShowMainMenu);
        
        if (btnPauseVR != null)
            btnPauseVR.onClick.RemoveListener(TogglePauseVR);
        
        if (soignantClient != null)
        {
            soignantClient.OnConnected -= OnHeadsetConnected;
            soignantClient.OnDisconnected -= OnHeadsetDisconnected;
        }
    }

    #region Network Events

    private void OnHeadsetConnected()
    {
        if (btnPauseVR != null)
            btnPauseVR.interactable = true;

        UpdateStatutCasqueButtonText();
    }

    private void OnHeadsetDisconnected()
    {
        if (btnPauseVR != null)
            btnPauseVR.interactable = false;

        UpdateStatutCasqueButtonText();
    }

    #endregion

    #region Navigation

    public void ShowMainMenu()
    {
        SetActivePanel(mainMenuPanel);
    }

    public void ShowStatutCasque()
    {
        if (IsHeadsetConnected())
            SetActivePanel(statutCasquePanel);
        else
            SetActivePanel(connexionCasquePanel);
    }

    private void SetActivePanel(GameObject panelToShow)
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(panelToShow == mainMenuPanel);
        
        if (statutCasquePanel != null)
            statutCasquePanel.SetActive(panelToShow == statutCasquePanel);

        if (connexionCasquePanel != null)
            connexionCasquePanel.SetActive(panelToShow == connexionCasquePanel);
    }

    #endregion

    #region Actions

    public void TogglePauseVR()
    {
        isPassthroughActive = !isPassthroughActive;
        
        if (useNetworkMode)
        {
            if (soignantClient != null && soignantClient.IsConnected)
            {
                soignantClient.SendCommand(isPassthroughActive
                    ? NetworkMessageType.EnablePassthrough
                    : NetworkMessageType.DisablePassthrough);
            }
            else
            {
                Debug.LogWarning("[SoignantUIManager] Aucun casque connecté !");
                isPassthroughActive = !isPassthroughActive;
            }
        }
        else
        {
            if (passthroughController != null)
                passthroughController.SetPassthroughActive(isPassthroughActive);
            else
                Debug.LogWarning("[SoignantUIManager] PassthroughController non assigné !");
        }

        UpdatePauseVRButtonText();
        Debug.Log($"[SoignantUIManager] Passthrough {(isPassthroughActive ? "ACTIVÉ" : "DÉSACTIVÉ")} (Mode: {(useNetworkMode ? "Réseau" : "Local")})");
    }

    private void UpdatePauseVRButtonText()
    {
        if (btnPauseVRText != null)
            btnPauseVRText.text = isPassthroughActive ? "Reprendre la VR" : "Mettre en pause la VR";
    }

    private bool IsHeadsetConnected()
    {
        if (useNetworkMode)
            return soignantClient != null && soignantClient.IsConnected;

        return passthroughController != null;
    }

    private void UpdateStatutCasqueButtonText()
    {
        if (btnStatutCasqueText == null)
            return;

        btnStatutCasqueText.text = IsHeadsetConnected() ? "Statut Casque" : "Connecter un casque";
    }

    #endregion
}
