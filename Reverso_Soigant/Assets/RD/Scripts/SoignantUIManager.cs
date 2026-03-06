using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gestionnaire de l'interface utilisateur côté soignant (PC).
/// Envoie les commandes au casque Quest via le réseau (SoignantClient).
/// Ce script fonctionne UNIQUEMENT en mode réseau.
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

    [Tooltip("Le panneau Vue Casque + Paramètres Séance (HeadsetViewPanel)")]
    [SerializeField] private GameObject sessionPanel;

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

    [Tooltip("Bouton pour ouvrir la vue casque + paramètres de séance")]
    [SerializeField] private Button btnSession;

    [Header("Réseau")]
    [Tooltip("Référence au client réseau")]
    [SerializeField] private SoignantClient soignantClient;

    private bool isPassthroughActive = false;

    private void Start()
    {
        // Trouver le SoignantClient
        if (soignantClient == null)
            soignantClient = FindFirstObjectByType<SoignantClient>();
        
        if (soignantClient == null)
        {
            Debug.LogError("[SoignantUIManager] SoignantClient non trouvé ! Ajoutez-le à la scène.");
            return;
        }

        // S'abonner aux events réseau
        soignantClient.OnConnected += OnHeadsetConnected;
        soignantClient.OnDisconnected += OnHeadsetDisconnected;
        
        // Configurer les boutons
        if (btnStatutCasque != null)
            btnStatutCasque.onClick.AddListener(ShowStatutCasque);
        
        if (btnRetourMenuStatut != null)
            btnRetourMenuStatut.onClick.AddListener(ShowMainMenu);

        if (btnRetourMenuConnexion != null)
            btnRetourMenuConnexion.onClick.AddListener(ShowMainMenu);
        
        if (btnPauseVR != null)
        {
            btnPauseVR.onClick.AddListener(TogglePauseVR);
            btnPauseVR.interactable = false; // Désactivé tant que pas connecté
        }

        if (btnSession != null)
        {
            btnSession.onClick.AddListener(ShowSessionPanel);
            btnSession.interactable = false;
        }

        ShowMainMenu();
        UpdatePauseVRButtonText();
        UpdateStatutCasqueButtonText();
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

        if (btnSession != null)
            btnSession.onClick.RemoveListener(ShowSessionPanel);
        
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
        if (btnSession != null)
            btnSession.interactable = true;

        UpdateStatutCasqueButtonText();
        Debug.Log("[SoignantUIManager] Casque connecté — commandes activées");
    }

    private void OnHeadsetDisconnected()
    {
        if (btnPauseVR != null)
            btnPauseVR.interactable = false;
        if (btnSession != null)
            btnSession.interactable = false;

        isPassthroughActive = false;
        UpdatePauseVRButtonText();
        UpdateStatutCasqueButtonText();
        Debug.Log("[SoignantUIManager] Casque déconnecté — commandes désactivées");
    }

    #endregion

    #region Navigation

    public void ShowMainMenu()
    {
        SetActivePanel(mainMenuPanel);
    }

    public void ShowStatutCasque()
    {
        if (soignantClient != null && soignantClient.IsConnected)
            SetActivePanel(statutCasquePanel);
        else
            SetActivePanel(connexionCasquePanel);
    }

    public void ShowSessionPanel()
    {
        if (soignantClient != null && soignantClient.IsConnected)
            SetActivePanel(sessionPanel);
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

        if (sessionPanel != null)
            sessionPanel.SetActive(panelToShow == sessionPanel);
    }

    #endregion

    #region Actions

    public void TogglePauseVR()
    {
        if (soignantClient == null || !soignantClient.IsConnected)
        {
            Debug.LogWarning("[SoignantUIManager] Aucun casque connecté !");
            return;
        }

        isPassthroughActive = !isPassthroughActive;
        
        soignantClient.SendCommand(isPassthroughActive
            ? NetworkMessageType.EnablePassthrough
            : NetworkMessageType.DisablePassthrough);

        UpdatePauseVRButtonText();
        Debug.Log($"[SoignantUIManager] Passthrough {(isPassthroughActive ? "ACTIVÉ" : "DÉSACTIVÉ")}");
    }

    private void UpdatePauseVRButtonText()
    {
        if (btnPauseVRText != null)
            btnPauseVRText.text = isPassthroughActive ? "Reprendre la VR" : "Mettre en pause la VR";
    }

    private void UpdateStatutCasqueButtonText()
    {
        if (btnStatutCasqueText == null)
            return;

        bool connected = soignantClient != null && soignantClient.IsConnected;
        btnStatutCasqueText.text = connected ? "Statut Casque" : "Connecter un casque";
    }

    #endregion
}
