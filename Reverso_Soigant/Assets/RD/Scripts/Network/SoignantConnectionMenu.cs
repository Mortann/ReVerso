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

    [Header("UI - Auto connexion")]
    [Tooltip("Active la connexion automatique au premier casque détecté")]
    [SerializeField] private Toggle toggleAutoConnect;
    [SerializeField] private bool autoConnectOnDiscover = false;

    [Header("Debug")]
    [SerializeField] private bool showNetworkDebugOverlay = true;

    private const string AutoConnectPrefKey = "Soignant.AutoConnectOnDiscover";
    private bool autoConnectInProgress = false;

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

        autoConnectOnDiscover = PlayerPrefs.GetInt(
            AutoConnectPrefKey,
            autoConnectOnDiscover ? 1 : 0) == 1;
        if (toggleAutoConnect != null)
        {
            toggleAutoConnect.SetIsOnWithoutNotify(autoConnectOnDiscover);
            toggleAutoConnect.onValueChanged.AddListener(OnAutoConnectChanged);
        }

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

        if (toggleAutoConnect != null)
            toggleAutoConnect.onValueChanged.RemoveListener(OnAutoConnectChanged);
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

    private void OnAutoConnectChanged(bool isOn)
    {
        autoConnectOnDiscover = isOn;
        PlayerPrefs.SetInt(AutoConnectPrefKey, isOn ? 1 : 0);
        PlayerPrefs.Save();

        TryAutoConnect();
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
        if (soignantClient == null) return;
        if (headsetListContainer == null)
        {
            Debug.LogWarning("[SoignantConnectionMenu] headsetListContainer non assigné.");
            return;
        }

        // Vider la liste actuelle
        foreach (Transform child in headsetListContainer)
            Destroy(child.gameObject);

        var headsets = soignantClient.DiscoveredHeadsets;

        // Message "aucun casque"
        if (txtNoHeadsetFound != null)
            txtNoHeadsetFound.gameObject.SetActive(headsets.Count == 0 && !soignantClient.IsConnected);

        TryAutoConnect();

        if (soignantClient.IsConnected)
            return; // Pas besoin d'afficher la liste si déjà connecté

        // Créer une entrée par casque découvert
        foreach (var h in headsets)
        {
            GameObject entry = CreateHeadsetEntry();
            if (entry == null) continue;

            // Texte
            var txt = entry.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt != null)
                txt.text = $"Casque — {h.ip}:{h.port}";

            // Bouton
            var btn = entry.GetComponent<Button>();
            if (btn == null) btn = entry.GetComponentInChildren<Button>(true);
            if (btn == null) btn = EnsureEntryButton(entry);

            if (btn != null)
            {
                string ip = h.ip;
                int port = h.port;
                btn.onClick.AddListener(() => OnHeadsetEntryClicked(ip, port));
            }
        }
    }

    private void TryAutoConnect()
    {
        if (!autoConnectOnDiscover || autoConnectInProgress)
            return;
        if (soignantClient == null || soignantClient.IsConnected)
            return;
        if (soignantClient.DiscoveredHeadsets == null || soignantClient.DiscoveredHeadsets.Count == 0)
            return;

        autoConnectInProgress = true;
        try
        {
            var h = soignantClient.DiscoveredHeadsets[0];
            soignantClient.ConnectToHeadset(h.ip, h.port);
        }
        finally
        {
            autoConnectInProgress = false;
        }
    }

    private GameObject CreateHeadsetEntry()
    {
        if (headsetEntryPrefab != null)
            return Instantiate(headsetEntryPrefab, headsetListContainer);

        Debug.LogWarning("[SoignantConnectionMenu] headsetEntryPrefab non assigné, création d'un bouton minimal.");
        return CreateFallbackEntry(headsetListContainer);
    }

    private Button EnsureEntryButton(GameObject entry)
    {
        if (entry == null) return null;

        var btn = entry.GetComponent<Button>();
        if (btn == null) btn = entry.AddComponent<Button>();

        var graphic = entry.GetComponent<Graphic>();
        if (graphic == null)
        {
            var image = entry.GetComponent<Image>();
            if (image == null) image = entry.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);
            graphic = image;
        }

        btn.targetGraphic = graphic;
        return btn;
    }

    private GameObject CreateFallbackEntry(Transform parent)
    {
        GameObject entry = new GameObject(
            "HeadsetEntry",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        entry.transform.SetParent(parent, false);

        var layout = entry.GetComponent<LayoutElement>();
        layout.preferredHeight = 56f;

        var image = entry.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.12f);

        var btn = entry.GetComponent<Button>();
        btn.targetGraphic = image;

        GameObject labelObj = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        labelObj.transform.SetParent(entry.transform, false);

        var labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 6f);
        labelRect.offsetMax = new Vector2(-12f, -6f);

        var label = labelObj.GetComponent<TextMeshProUGUI>();
        label.fontSize = 24f;
        label.enableAutoSizing = true;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        return entry;
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

    // ─────────────────────────── DEBUG OVERLAY ───────────────────────────

    private void OnGUI()
    {
        if (!showNetworkDebugOverlay) return;

        var logs = SoignantClient.DebugLog;
        if (logs == null || logs.Count == 0) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 14 };
        GUIStyle logStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = Color.cyan },
            wordWrap = true
        };

        float width = 500;
        float lineHeight = 18;
        float height = 30 + logs.Count * lineHeight;
        float x = Screen.width - width - 10;
        float y = 10;

        GUI.Box(new Rect(x, y, width, height), "Network Debug Log", boxStyle);
        for (int i = 0; i < logs.Count; i++)
        {
            GUI.Label(new Rect(x + 8, y + 22 + i * lineHeight, width - 16, lineHeight),
                logs[i], logStyle);
        }
    }
}
