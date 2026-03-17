using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ReVerso.Data;

/// <summary>
/// Exemple d'intégration UI complète du système de séances.
/// À utiliser côté PC (Soignant) pour contrôler les séances.
/// 
/// SETUP :
/// 1. Ajouter ce script sur un Canvas
/// 2. Assigner les références UI dans l'Inspector
/// 3. SessionControllerPC sera trouvé automatiquement
/// </summary>
public class SessionExample : MonoBehaviour
{
    [Header("UI - Sélection Patient")]
    [SerializeField] private TMP_Dropdown patientDropdown;
    [SerializeField] private TextMeshProUGUI patientInfoText;
    [SerializeField] private Button loadPatientButton;

    [Header("UI - Contrôle Séance")]
    [SerializeField] private Button startSessionButton;
    [SerializeField] private Button stopSessionButton;
    [SerializeField] private Button pauseSessionButton;
    [SerializeField] private Button resumeSessionButton;

    [Header("UI - État")]
    [SerializeField] private TextMeshProUGUI connectionStatusText;
    [SerializeField] private TextMeshProUGUI sessionStateText;
    [SerializeField] private TextMeshProUGUI phaseProgressText;
    [SerializeField] private Slider progressBar;

    [Header("UI - Métriques Temps Réel")]
    [SerializeField] private TextMeshProUGUI amplitudeInitialeText;
    [SerializeField] private TextMeshProUGUI amplitudeFinaleText;

    [Header("UI - Résultats")]
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TextMeshProUGUI resultsText;
    [SerializeField] private TextMeshProUGUI progressionText;
    [SerializeField] private TextMeshProUGUI ameliorationText;

    private PatientProfile selectedPatient;

    #region Unity Lifecycle

    private void Start()
    {
        // Setup boutons
        startSessionButton.onClick.AddListener(OnStartSessionClicked);
        stopSessionButton.onClick.AddListener(OnStopSessionClicked);
        pauseSessionButton.onClick.AddListener(OnPauseSessionClicked);
        resumeSessionButton.onClick.AddListener(OnResumeSessionClicked);
        loadPatientButton.onClick.AddListener(OnLoadPatientClicked);

        // Setup événements SessionController
        SessionControllerPC.Instance.OnRemoteStateChanged += OnSessionStateChanged;
        SessionControllerPC.Instance.OnRemoteProgress += OnSessionProgressChanged;
        SessionControllerPC.Instance.OnCaptureInitialeComplete += OnCaptureInitialeComplete;
        SessionControllerPC.Instance.OnCaptureFinaleComplete += OnCaptureFinaleComplete;
        SessionControllerPC.Instance.OnSessionCompleted += OnSessionCompleted;

        // Charger la liste des patients
        LoadPatientList();

        // État initial
        UpdateUIState();
        resultsPanel.SetActive(false);
    }

    private void Update()
    {
        // Mettre à jour l'état de connexion
        bool isConnected = SessionControllerPC.Instance.IsHeadsetConnected();
        connectionStatusText.text = isConnected ? "✅ Casque connecté" : "❌ Casque non connecté";
        connectionStatusText.color = isConnected ? Color.green : Color.red;

        // Mettre à jour l'état des boutons en continu
        UpdateButtonStates();
    }

    private void OnDestroy()
    {
        // Se désabonner
        if (SessionControllerPC.Instance != null)
        {
            SessionControllerPC.Instance.OnRemoteStateChanged -= OnSessionStateChanged;
            SessionControllerPC.Instance.OnRemoteProgress -= OnSessionProgressChanged;
            SessionControllerPC.Instance.OnCaptureInitialeComplete -= OnCaptureInitialeComplete;
            SessionControllerPC.Instance.OnCaptureFinaleComplete -= OnCaptureFinaleComplete;
            SessionControllerPC.Instance.OnSessionCompleted -= OnSessionCompleted;
        }
    }

    #endregion

    #region Patient Selection

    private void LoadPatientList()
    {
        patientDropdown.ClearOptions();

        var patients = PatientDataManager.Instance.GetAllPatients();
        
        if (patients.Count == 0)
        {
            patientDropdown.AddOptions(new System.Collections.Generic.List<string> { "Aucun patient" });
            patientDropdown.interactable = false;
            return;
        }

        var options = new System.Collections.Generic.List<string>();
        foreach (var patient in patients)
        {
            options.Add($"{patient.infos_personnelles.NomComplet} ({patient.num_dossier})");
        }

        patientDropdown.AddOptions(options);
        patientDropdown.interactable = true;
    }

    private void OnLoadPatientClicked()
    {
        if (patientDropdown.value < 0) return;

        var patients = PatientDataManager.Instance.GetAllPatients();
        if (patientDropdown.value >= patients.Count) return;

        selectedPatient = patients[patientDropdown.value];
        PatientDataManager.Instance.SetCurrentPatient(selectedPatient.num_dossier);

        // Afficher les infos
        patientInfoText.text = $"<b>{selectedPatient.infos_personnelles.NomComplet}</b>\n" +
                              $"Dossier : {selectedPatient.num_dossier}\n" +
                              $"Côté affecté : {selectedPatient.profil_medical.cote_affecte}\n" +
                              $"Séances : {selectedPatient.data.Count}";

        Debug.Log($"[SessionExample] Patient sélectionné : {selectedPatient.infos_personnelles.NomComplet}");

        UpdateButtonStates();
    }

    #endregion

    #region Session Control

    private void OnStartSessionClicked()
    {
        // Vérifications
        if (!SessionControllerPC.Instance.IsHeadsetConnected())
        {
            Debug.LogWarning("[SessionExample] Casque non connecté !");
            return;
        }

        if (selectedPatient == null)
        {
            Debug.LogWarning("[SessionExample] Aucun patient sélectionné !");
            return;
        }

        if (SessionControllerPC.Instance.IsSessionActive)
        {
            Debug.LogWarning("[SessionExample] Une séance est déjà en cours !");
            return;
        }

        // Démarrer
        Debug.Log($"[SessionExample] 🎬 Démarrage séance pour {selectedPatient.infos_personnelles.NomComplet}");
        SessionControllerPC.Instance.StartSession(selectedPatient);

        // Masquer les résultats précédents
        resultsPanel.SetActive(false);
        amplitudeInitialeText.text = "En attente...";
        amplitudeFinaleText.text = "En attente...";
    }

    private void OnStopSessionClicked()
    {
        Debug.Log("[SessionExample] ⛔ KillSwitch activé");
        SessionControllerPC.Instance.StopSession();
    }

    private void OnPauseSessionClicked()
    {
        Debug.Log("[SessionExample] ⏸️ Pause");
        SessionControllerPC.Instance.PauseSession();
    }

    private void OnResumeSessionClicked()
    {
        Debug.Log("[SessionExample] ▶️ Reprise");
        SessionControllerPC.Instance.ResumeSession();
    }

    #endregion

    #region Session Events

    private void OnSessionStateChanged(SessionState newState)
    {
        string phaseName = SessionControllerPC.Instance.GetCurrentPhaseName();
        string phaseDesc = SessionControllerPC.Instance.GetCurrentPhaseDescription();

        sessionStateText.text = $"<b>{phaseName}</b>\n{phaseDesc}";

        Debug.Log($"[SessionExample] Phase : {phaseName}");

        UpdateUIState();
    }

    private void OnSessionProgressChanged(float progress)
    {
        progressBar.value = progress;
        phaseProgressText.text = $"{progress * 100f:F0}%";
    }

    private void OnCaptureInitialeComplete(float amplitude)
    {
        amplitudeInitialeText.text = $"<b>Initiale :</b> {amplitude:F3} m";
        amplitudeInitialeText.color = Color.cyan;

        Debug.Log($"[SessionExample] 📊 Amplitude initiale : {amplitude:F3}");
    }

    private void OnCaptureFinaleComplete(float amplitude)
    {
        amplitudeFinaleText.text = $"<b>Finale :</b> {amplitude:F3} m";
        amplitudeFinaleText.color = Color.green;

        Debug.Log($"[SessionExample] 📊 Amplitude finale : {amplitude:F3}");
    }

    private void OnSessionCompleted(SessionResults results)
    {
        Debug.Log($"[SessionExample] ✅ Séance terminée !");

        // Afficher le panneau résultats
        resultsPanel.SetActive(true);

        // Détails complets
        resultsText.text = $"<size=18><b>Séance terminée</b></size>\n\n" +
                          $"<b>Patient :</b> {selectedPatient.infos_personnelles.NomComplet}\n" +
                          $"<b>Date :</b> {results.date_session}\n" +
                          $"<b>Durée :</b> {results.duree_totale_secondes / 60f:F1} min\n\n" +
                          $"<b>Amplitude initiale :</b> {results.amplitude_initiale:F3} m\n" +
                          $"<b>Amplitude finale :</b> {results.amplitude_finale:F3} m\n";

        // Progression
        Color progressionColor = results.progression_pourcent >= 0 ? Color.green : Color.red;
        string progressionSymbol = results.progression_pourcent >= 0 ? "📈" : "📉";
        
        progressionText.text = $"{progressionSymbol} <b>{results.progression_pourcent:F1}%</b>";
        progressionText.color = progressionColor;

        // Amélioration
        if (results.amelioration_detectee)
        {
            ameliorationText.text = "✅ <b>Amélioration détectée</b>";
            ameliorationText.color = Color.green;
        }
        else
        {
            ameliorationText.text = "⚠️ Pas d'amélioration significative";
            ameliorationText.color = Color.yellow;
        }

        // Le SessionControllerPC a déjà sauvegardé automatiquement !
        Debug.Log("[SessionExample] 💾 Résultats déjà enregistrés dans la base de données");
    }

    #endregion

    #region UI State Management

    private void UpdateUIState()
    {
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        bool isConnected = SessionControllerPC.Instance.IsHeadsetConnected();
        bool isSessionActive = SessionControllerPC.Instance.IsSessionActive;
        bool hasPatient = selectedPatient != null;

        // Start : disponible si connecté, patient sélectionné, et pas de séance active
        startSessionButton.interactable = isConnected && hasPatient && !isSessionActive;

        // Stop : disponible uniquement si séance active
        stopSessionButton.interactable = isSessionActive;

        // Pause/Resume : disponibles si séance active (à améliorer avec état pause/resume)
        pauseSessionButton.interactable = isSessionActive;
        resumeSessionButton.interactable = isSessionActive;

        // Sélection patient : désactivée pendant séance
        patientDropdown.interactable = !isSessionActive;
        loadPatientButton.interactable = !isSessionActive;
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Méthode utilitaire pour afficher l'historique du patient sélectionné
    /// </summary>
    public void ShowPatientHistory()
    {
        if (selectedPatient == null)
        {
            Debug.Log("Aucun patient sélectionné");
            return;
        }

        Debug.Log($"--- Historique de {selectedPatient.infos_personnelles.NomComplet} ---");
        
        foreach (var session in selectedPatient.data)
        {
            Debug.Log($"Séance {session.id_seance} - {session.date_seance}\n" +
                     $"  Amplitude initiale : {session.metriques.moy_amplitude_initiale:F3}\n" +
                     $"  Amplitude finale : {session.metriques.moy_amplitude_finale:F3}");
        }
    }

    /// <summary>
    /// Créer un patient de test (pour la démo)
    /// </summary>
    public void CreateTestPatient()
    {
        var testPatient = PatientDataManager.Instance.CreatePatient(
            "TEST001",
            "Doe",
            "John",
            "1980-01-15"
        );

        testPatient.preferences.environnement_favori = Environnement.Foret;
        testPatient.preferences.apparence_guide = ApparenceGuide.Masculin;

        PatientDataManager.Instance.SaveToFile();

        Debug.Log($"[SessionExample] Patient de test créé : {testPatient.num_dossier}");

        // Recharger la liste
        LoadPatientList();
    }

    #endregion
}
