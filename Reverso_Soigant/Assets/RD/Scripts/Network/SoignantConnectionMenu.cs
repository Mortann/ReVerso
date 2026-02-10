using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu de connexion casque côté PC soignant.
/// Affiche les casques découverts sur le réseau, permet de s'y connecter,
/// et montre l'état de la connexion.
/// </summary>
public class SoignantConnectionMenu : MonoBehaviour
{
    [Header("Réseau")]
    [SerializeField] private SoignantClient soignantClient;

    [Header("UI - Statut")]
    [Tooltip("Statut global de la connexion (Recherche / Connecté / Erreur)")]
    [SerializeField] private TextMeshProUGUI txtConnectionStatus;
    [Tooltip("IP du casque connecté")]
    [SerializeField] private TextMeshProUGUI txtHeadsetIP;
    [Tooltip("Dernier message reçu du casque")]
    [SerializeField] private TextMeshProUGUI txtLastMessage;

    [Header("UI - Indicateur")]
    [SerializeField] private Image imgConnectionIndicator;
    [SerializeField] private Color colorOk = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color colorWarn = new Color(0.95f, 0.8f, 0.2f);
    [SerializeField] private Color colorError = new Color(0.9f, 0.2f, 0.2f);

    [Header("UI - Liste casques découverts")]
    [Tooltip("Conteneur parent pour les entrées de casques (layout vertical)")]
    [SerializeField] private Transform headsetListContainer;
    [Tooltip("Prefab d'une entrée casque (un bouton avec du texte)")]
    [SerializeField] private GameObject headsetEntryPrefab;
    [Tooltip("Texte affiché quand aucun casque n'est trouvé")]
    [SerializeField] private TextMeshProUGUI txtNoHeadsetFound;

    [Header("UI - Boutons")]
    [Tooltip("Bouton pour rafraîchir la recherche")]
    [SerializeField] private Button btnRefresh;
    [Tooltip("Bouton pour se déconnecter")]
    [SerializeField] private Button btnDisconnect;
    [Tooltip("Bouton pour demander le statut du casque")]
    [SerializeField] private Button btnRequestStatus;

    private void Start()
    {
        if (soignantClient == null)
            soignantClient = FindFirstObjectByType<SoignantClient>();

        if (soignantClient == null)
        {
            Debug.LogError("[SoignantConnectionMenu] SoignantClient non trouvé !");
            SetStatus("❌ SoignantClient manquant", colorError);
            return;
        }

        soignantClient.OnStatusChanged += OnStatusChanged;
        soignantClient.OnConnected += OnConnected;
        soignantClient.OnDisconnected += OnDisconnected;
        soignantClient.OnHeadsetListChanged += RefreshHeadsetList;
        soignantClient.OnMessageReceived += OnMessageReceived;

        if (btnRefresh != null)
            btnRefresh.onClick.AddListener(OnRefreshClicked);
        if (btnDisconnect != null)
            btnDisconnect.onClick.AddListener(OnDisconnectClicked);
        if (btnRequestStatus != null)
            btnRequestStatus.onClick.AddListener(OnRequestStatusClicked);

        RefreshAllUI();
    }

    private void OnDestroy()
    {
        if (soignantClient != null)
        {
            soignantClient.OnStatusChanged -= OnStatusChanged;
            soignantClient.OnConnected -= OnConnected;
            soignantClient.OnDisconnected -= OnDisconnected;
            soignantClient.OnHeadsetListChanged -= RefreshHeadsetList;
            soignantClient.OnMessageReceived -= OnMessageReceived;
        }

        if (btnRefresh != null)
            btnRefresh.onClick.RemoveListener(OnRefreshClicked);
        if (btnDisconnect != null)
            btnDisconnect.onClick.RemoveListener(OnDisconnectClicked);
        if (btnRequestStatus != null)
            btnRequestStatus.onClick.RemoveListener(OnRequestStatusClicked);
    }

    // ─────────────────────────── EVENTS ───────────────────────────

    private void OnStatusChanged(string status)
    {
        if (txtConnectionStatus != null)
            txtConnectionStatus.text = status;

        if (imgConnectionIndicator != null)
        {
            if (status.Contains("✅")) imgConnectionIndicator.color = colorOk;
            else if (status.Contains("❌")) imgConnectionIndicator.color = colorError;
            else imgConnectionIndicator.color = colorWarn;
        }
    }

    private void OnConnected()
    {
        SetStatus($"✅ Connecté au casque: {soignantClient.ConnectedHeadsetIP}", colorOk);
        UpdateHeadsetIP(soignantClient.ConnectedHeadsetIP);
        UpdateButtons();
    }

    private void OnDisconnected()
    {
        SetStatus("🔍 Recherche de casques...", colorWarn);
        UpdateHeadsetIP("-");
        UpdateButtons();
        RefreshHeadsetList();
    }

    private void OnMessageReceived(NetworkMessage message)
    {
        if (txtLastMessage != null)
            txtLastMessage.text = $"Dernier msg: {message.type} — {message.data}";
    }

    // ─────────────────────────── BOUTONS ───────────────────────────

    private void OnRefreshClicked()
    {
        soignantClient.StartDiscovery();
        RefreshHeadsetList();
    }

    private void OnDisconnectClicked()
    {
        soignantClient.Disconnect();
    }

    private void OnRequestStatusClicked()
    {
        if (soignantClient != null && soignantClient.IsConnected)
            soignantClient.SendCommand(NetworkMessageType.RequestStatus);
    }

    /// <summary>
    /// Appelé quand le soignant clique sur un casque dans la liste.
    /// </summary>
    private void OnHeadsetEntryClicked(string ip, int port)
    {
        soignantClient.ConnectToHeadset(ip, port);
    }

    // ─────────────────────────── UI ───────────────────────────

    private void RefreshAllUI()
    {
        if (soignantClient == null) return;

        OnStatusChanged(soignantClient.ConnectionStatus);
        UpdateHeadsetIP(soignantClient.IsConnected ? soignantClient.ConnectedHeadsetIP : "-");
        UpdateButtons();
        RefreshHeadsetList();
    }

    private void RefreshHeadsetList()
    {
        if (headsetListContainer == null) return;

        // Vider la liste actuelle
        foreach (Transform child in headsetListContainer)
            Destroy(child.gameObject);

        var headsets = soignantClient.DiscoveredHeadsets;

        // Message "aucun casque"
        if (txtNoHeadsetFound != null)
            txtNoHeadsetFound.gameObject.SetActive(headsets.Count == 0 && !soignantClient.IsConnected);

        if (soignantClient.IsConnected)
            return; // Pas besoin d'afficher la liste si déjà connecté

        // Créer une entrée par casque découvert
        foreach (var h in headsets)
        {
            if (headsetEntryPrefab != null)
            {
                GameObject entry = Instantiate(headsetEntryPrefab, headsetListContainer);

                // Texte
                var txt = entry.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null)
                    txt.text = $"Casque — {h.ip}:{h.port}";

                // Bouton
                var btn = entry.GetComponent<Button>();
                if (btn == null) btn = entry.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    string ip = h.ip;
                    int port = h.port;
                    btn.onClick.AddListener(() => OnHeadsetEntryClicked(ip, port));
                }
            }
            else
            {
                Debug.LogWarning("[SoignantConnectionMenu] headsetEntryPrefab non assigné, impossible d'afficher la liste.");
            }
        }
    }

    private void SetStatus(string text, Color indicatorColor)
    {
        if (txtConnectionStatus != null)
            txtConnectionStatus.text = text;
        if (imgConnectionIndicator != null)
            imgConnectionIndicator.color = indicatorColor;
    }

    private void UpdateHeadsetIP(string ip)
    {
        if (txtHeadsetIP != null)
            txtHeadsetIP.text = ip;
    }

    private void UpdateButtons()
    {
        bool connected = soignantClient != null && soignantClient.IsConnected;

        if (btnDisconnect != null) btnDisconnect.interactable = connected;
        if (btnRequestStatus != null) btnRequestStatus.interactable = connected;
        if (btnRefresh != null) btnRefresh.interactable = !connected;
    }
}
