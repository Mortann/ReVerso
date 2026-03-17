using System;
using System.Collections;
using UnityEngine;
using ReVerso.Data;

/// <summary>
/// Gestionnaire de séance côté PATIENT (Quest).
/// Orchestre toutes les phases d'une séance de thérapie miroir.

/// COMMUNICATION RÉSEAU :
/// - Reçoit : StartSession, StopSession (killSwitch)
/// - Envoie : SessionStateChanged, SessionProgress, SessionCompleted
/// </summary>
public class SessionManager : MonoBehaviour
{
    #region Singleton

    private static SessionManager _instance;
    public static SessionManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SessionManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("SessionManager");
                    _instance = go.AddComponent<SessionManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    #endregion

    #region Inspector Fields

    [Header("Références Obligatoires")]
    [Tooltip("Collecteur de métriques pour mesurer l'amplitude")]
    [SerializeField] private SessionMetricsCollector metricsCollector;
    
    [Tooltip("Client réseau pour communiquer avec le PC")]
    [SerializeField] private NetworkClient networkClient;
    
    [Tooltip("Handler pour lire les données de tracking")]
    [SerializeField] private HandTrackingDebugger handTracker;

    [Header("Références Scène (optionnel - à setup)")]
    [Tooltip("Manager de l'environnement VR (forêt/montagne/intérieur)")]
    [SerializeField] private GameObject environmentManager;
    
    [Tooltip("Entraîneur virtuel")]
    [SerializeField] private GameObject virtualTrainer;
    
    [Tooltip("Système de mirroring de la main")]
    [SerializeField] private GameObject handMirrorSystem;

    [Header("État de la Séance")]
    [SerializeField] private SessionState currentState = SessionState.Idle;
    [SerializeField] private SessionConfig currentConfig;
    [SerializeField] private float phaseTimer = 0f;
    [SerializeField] private float phaseDuration = 0f;

    #endregion

    #region Private Fields

    private SessionResults currentResults;
    private float sessionStartTime;
    private Coroutine currentPhaseCoroutine;
    private bool isKillSwitchActivated = false;

    #endregion

    #region Events

    /// <summary>
    /// Événement déclenché quand l'état de la séance change
    /// </summary>
    public event Action<SessionState> OnStateChanged;
    
    /// <summary>
    /// Événement déclenché pour la progression d'une phase (0.0 à 1.0)
    /// </summary>
    public event Action<float> OnPhaseProgress;
    
    /// <summary>
    /// Événement déclenché quand la séance est terminée
    /// </summary>
    public event Action<SessionResults> OnSessionCompleted;

    #endregion

    #region Properties

    public SessionState CurrentState => currentState;
    public SessionConfig CurrentConfig => currentConfig;
    public float PhaseProgress => phaseDuration > 0 ? Mathf.Clamp01(phaseTimer / phaseDuration) : 0f;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        // Trouver les composants automatiquement si non assignés
        if (metricsCollector == null)
            metricsCollector = FindFirstObjectByType<SessionMetricsCollector>();
        
        if (networkClient == null)
            networkClient = FindFirstObjectByType<NetworkClient>();
        
        if (handTracker == null)
            handTracker = FindFirstObjectByType<HandTrackingDebugger>();

        // Vérifications
        if (metricsCollector == null)
            Debug.LogError("[SessionManager] SessionMetricsCollector non trouvé !");
        
        if (networkClient == null)
            Debug.LogWarning("[SessionManager] NetworkClient non trouvé - mode standalone");
    }

    private void OnEnable()
    {
        // S'abonner aux messages réseau
        if (networkClient != null)
        {
            networkClient.OnMessageReceived += OnNetworkMessageReceived;
        }
    }

    private void OnDisable()
    {
        // Se désabonner
        if (networkClient != null)
        {
            networkClient.OnMessageReceived -= OnNetworkMessageReceived;
        }
    }

    private void Update()
    {
        // Mettre à jour le timer de phase si une séance est en cours
        if (currentState != SessionState.Idle && 
            currentState != SessionState.Completed && 
            currentState != SessionState.Interrupted)
        {
            phaseTimer += Time.deltaTime;
            
            // Notifier la progression
            OnPhaseProgress?.Invoke(PhaseProgress);
        }
    }

    #endregion

    #region Session Control

    /// <summary>
    /// Démarre une nouvelle séance avec la configuration donnée
    /// </summary>
    public void StartSession(SessionConfig config)
    {
        if (currentState != SessionState.Idle)
        {
            Debug.LogWarning("[SessionManager] Une séance est déjà en cours !");
            return;
        }

        currentConfig = config;
        currentResults = new SessionResults
        {
            num_dossier = config.num_dossier,
            date_session = config.date_debut
        };
        
        sessionStartTime = Time.time;
        isKillSwitchActivated = false;

        Debug.Log($"[SessionManager] 🎬 Démarrage séance pour patient {config.num_dossier}");
        
        // Démarrer le workflow
        StartCoroutine(SessionWorkflow());
    }

    /// <summary>
    /// Arrête la séance immédiatement (killSwitch)
    /// </summary>
    public void StopSession()
    {
        Debug.LogWarning("[SessionManager] ⛔ KillSwitch activé - Arrêt de la séance");
        
        isKillSwitchActivated = true;
        
        // Arrêter la coroutine en cours
        if (currentPhaseCoroutine != null)
        {
            StopCoroutine(currentPhaseCoroutine);
        }

        // Arrêter l'enregistrement si en cours
        if (metricsCollector.IsRecording())
        {
            metricsCollector.StopRecordingAndGetAverage();
        }

        ChangeState(SessionState.Interrupted);
        
        // Notifier le PC
        SendNetworkMessage(NetworkMessageType.SessionStateChanged, SessionState.Interrupted.ToString());
        
        // Réinitialiser
        ResetSession();
    }

    #endregion

    #region Session Workflow

    /// <summary>
    /// Workflow complet de la séance
    /// </summary>
    private IEnumerator SessionWorkflow()
    {
        // === PHASE 1 : PRÉPARATION ===
        yield return StartCoroutine(Phase_Preparation());
        if (isKillSwitchActivated) yield break;

        // === PHASE 2 : CAPTURE INITIALE ===
        yield return StartCoroutine(Phase_CaptureInitiale());
        if (isKillSwitchActivated) yield break;

        // === PHASE 3 : PRÉ-EXERCICE (optionnel) ===
        if (currentConfig.active_pre_exercice)
        {
            yield return StartCoroutine(Phase_PreExercice());
            if (isKillSwitchActivated) yield break;
        }

        // === PHASE 4 : THÉRAPIE MIROIR ===
        yield return StartCoroutine(Phase_TherapieMiroir());
        if (isKillSwitchActivated) yield break;

        // === PHASE 5 : CAPTURE FINALE ===
        yield return StartCoroutine(Phase_CaptureFinale());
        if (isKillSwitchActivated) yield break;

        // === PHASE 6 : RÉSULTATS ===
        yield return StartCoroutine(Phase_Resultats());
        
        // === PHASE 7 : TERMINÉ ===
        ChangeState(SessionState.Completed);
        OnSessionCompleted?.Invoke(currentResults);
        
        Debug.Log($"[SessionManager] ✅ Séance terminée ! Progression : {currentResults.progression_pourcent:F1}%");
        
        // Réinitialiser pour la prochaine séance
        ResetSession();
    }

    #endregion

    #region Phase Implementations

    /// <summary>
    /// PHASE 1 : Préparation (setup environnement, préférences)
    /// </summary>
    private IEnumerator Phase_Preparation()
    {
        ChangeState(SessionState.Preparing);
        SetPhaseDuration(3f); // 3 secondes de setup
        
        Debug.Log($"[SessionManager] 🔧 Préparation - Environnement: {currentConfig.environnement}, Guide: {currentConfig.apparence_guide}");

        // TODO: Charger l'environnement selon préférences
        // if (environmentManager != null)
        //     environmentManager.LoadEnvironment(currentConfig.environnement);

        // TODO: Configurer l'apparence du guide
        // if (virtualTrainer != null)
        //     virtualTrainer.SetAppearance(currentConfig.apparence_guide);

        yield return new WaitForSeconds(3f);
    }

    /// <summary>
    /// PHASE 2 : Capture initiale (10-20s)
    /// </summary>
    private IEnumerator Phase_CaptureInitiale()
    {
        ChangeState(SessionState.CaptureInitiale);
        SetPhaseDuration(currentConfig.duree_capture_initiale);
        
        Debug.Log($"[SessionManager] 📊 Capture initiale ({currentConfig.duree_capture_initiale}s) - Main {currentConfig.cote_affecte}");

        // TODO: Afficher instructions au patient
        // "Essayez de bouger vos deux mains pendant {X} secondes"

        // Démarrer l'enregistrement
        metricsCollector.StartRecording(currentConfig.cote_affecte, currentConfig.duree_capture_initiale);

        // Attendre la fin
        yield return new WaitForSeconds(currentConfig.duree_capture_initiale);

        // Récupérer l'amplitude
        currentResults.amplitude_initiale = metricsCollector.StopRecordingAndGetAverage();
        
        Debug.Log($"[SessionManager] Amplitude initiale : {currentResults.amplitude_initiale:F3}");
        
        // Notifier le PC
        SendNetworkMessage(NetworkMessageType.CaptureInitialeComplete, currentResults.amplitude_initiale.ToString());
    }

    /// <summary>
    /// PHASE 3 : Pré-exercice de respiration (optionnel)
    /// </summary>
    private IEnumerator Phase_PreExercice()
    {
        ChangeState(SessionState.PreExercice);
        SetPhaseDuration(120f); // 2 minutes (2 exercices)
        
        Debug.Log("[SessionManager] 🧘 Pré-exercice - Respiration guidée");

        // TODO: Lancer les exercices de respiration
        // 2 exercices de respiration avec l'entraîneur

        yield return new WaitForSeconds(120f);
    }

    /// <summary>
    /// PHASE 4 : Thérapie miroir (exercice principal)
    /// </summary>
    private IEnumerator Phase_TherapieMiroir()
    {
        ChangeState(SessionState.TherapieMiroir);
        SetPhaseDuration(currentConfig.duree_therapie_miroir);
        
        Debug.Log($"[SessionManager] 🪞 Thérapie miroir ({currentConfig.duree_therapie_miroir / 60f:F1} min)");

        // TODO: Activer le système de mirroring
        // if (handMirrorSystem != null)
        //     handMirrorSystem.SetActive(true);
        //     handMirrorSystem.SetMirroredHand(currentConfig.cote_affecte);

        // TODO: Entraîneur donne instructions
        // virtualTrainer.StartGuidedExercise();

        yield return new WaitForSeconds(currentConfig.duree_therapie_miroir);

        // TODO: Désactiver le mirroring
        // if (handMirrorSystem != null)
        //     handMirrorSystem.SetActive(false);
    }

    /// <summary>
    /// PHASE 5 : Capture finale (comparaison avec initiale)
    /// </summary>
    private IEnumerator Phase_CaptureFinale()
    {
        ChangeState(SessionState.CaptureFinale);
        SetPhaseDuration(currentConfig.duree_capture_finale);
        
        Debug.Log($"[SessionManager] 📊 Capture finale ({currentConfig.duree_capture_finale}s)");

        // TODO: Afficher instructions
        // "Essayez de refaire les mêmes mouvements"

        // Démarrer l'enregistrement
        metricsCollector.StartRecording(currentConfig.cote_affecte, currentConfig.duree_capture_finale);

        yield return new WaitForSeconds(currentConfig.duree_capture_finale);

        // Récupérer l'amplitude finale
        currentResults.amplitude_finale = metricsCollector.StopRecordingAndGetAverage();
        
        Debug.Log($"[SessionManager] Amplitude finale : {currentResults.amplitude_finale:F3}");
        
        // Notifier le PC
        SendNetworkMessage(NetworkMessageType.CaptureFinaleComplete, currentResults.amplitude_finale.ToString());
    }

    /// <summary>
    /// PHASE 6 : Calcul et affichage des résultats
    /// </summary>
    private IEnumerator Phase_Resultats()
    {
        ChangeState(SessionState.Resultats);
        SetPhaseDuration(5f);

        // Calculer les résultats
        currentResults.duree_totale_secondes = Time.time - sessionStartTime;
        
        if (currentResults.amplitude_initiale > 0)
        {
            currentResults.progression_pourcent = 
                ((currentResults.amplitude_finale - currentResults.amplitude_initiale) / currentResults.amplitude_initiale) * 100f;
        }
        else
        {
            currentResults.progression_pourcent = 0f;
        }

        currentResults.amelioration_detectee = currentResults.amplitude_finale > currentResults.amplitude_initiale;

        Debug.Log($"[SessionManager] 📈 Résultats :\n" +
                  $"  Amplitude initiale : {currentResults.amplitude_initiale:F3}\n" +
                  $"  Amplitude finale : {currentResults.amplitude_finale:F3}\n" +
                  $"  Progression : {currentResults.progression_pourcent:F1}%\n" +
                  $"  Amélioration : {(currentResults.amelioration_detectee ? "✅" : "❌")}\n" +
                  $"  Durée totale : {currentResults.duree_totale_secondes / 60f:F1} min");

        // Envoyer les résultats au PC
        string resultsJson = JsonUtility.ToJson(currentResults);
        SendNetworkMessage(NetworkMessageType.SessionCompleted, resultsJson);

        // TODO: Afficher les résultats au patient
        // "Félicitations ! Vous avez progressé de X%"

        yield return new WaitForSeconds(5f);
    }

    #endregion

    #region State Management

    /// <summary>
    /// Change l'état de la séance et notifie
    /// </summary>
    private void ChangeState(SessionState newState)
    {
        if (currentState == newState) return;

        SessionState previousState = currentState;
        currentState = newState;

        Debug.Log($"[SessionManager] État : {previousState} → {newState}");

        // Notifier localement
        OnStateChanged?.Invoke(newState);

        // Notifier le PC via réseau
        SendNetworkMessage(NetworkMessageType.SessionStateChanged, newState.ToString());
    }

    /// <summary>
    /// Définit la durée de la phase actuelle
    /// </summary>
    private void SetPhaseDuration(float duration)
    {
        phaseDuration = duration;
        phaseTimer = 0f;
    }

    /// <summary>
    /// Réinitialise la séance
    /// </summary>
    private void ResetSession()
    {
        currentState = SessionState.Idle;
        currentConfig = null;
        currentResults = null;
        phaseTimer = 0f;
        phaseDuration = 0f;
        isKillSwitchActivated = false;
    }

    #endregion

    #region Network Communication

    /// <summary>
    /// Gestionnaire de messages réseau reçus du PC
    /// </summary>
    private void OnNetworkMessageReceived(NetworkMessage message)
    {
        switch (message.type)
        {
            case NetworkMessageType.StartSession:
                // Désérialiser la config et démarrer
                SessionConfig config = JsonUtility.FromJson<SessionConfig>(message.data);
                StartSession(config);
                break;

            case NetworkMessageType.StopSession:
                // KillSwitch activé
                StopSession();
                break;

            case NetworkMessageType.PauseSession:
                // TODO: Implémenter pause
                Debug.Log("[SessionManager] Pause demandée (non implémenté)");
                break;

            case NetworkMessageType.ResumeSession:
                // TODO: Implémenter reprise
                Debug.Log("[SessionManager] Reprise demandée (non implémenté)");
                break;
        }
    }

    /// <summary>
    /// Envoie un message au PC
    /// </summary>
    private void SendNetworkMessage(NetworkMessageType type, string data = "")
    {
        if (networkClient == null || !networkClient.IsConnected)
        {
            Debug.LogWarning("[SessionManager] Impossible d'envoyer le message - Pas de connexion réseau");
            return;
        }

        NetworkMessage message = new NetworkMessage(type, data);
        networkClient.SendMessage(message);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Démarre une séance manuellement (pour tests sans réseau)
    /// </summary>
    public void StartSessionLocal(string numDossier, CoteAffecte coteAffecte)
    {
        SessionConfig config = new SessionConfig(new PatientProfile(numDossier))
        {
            cote_affecte = coteAffecte
        };

        StartSession(config);
    }

    /// <summary>
    /// Récupère l'état actuel de la séance
    /// </summary>
    public SessionPhaseData GetCurrentPhaseData()
    {
        return new SessionPhaseData(currentState, phaseDuration)
        {
            progress = PhaseProgress
        };
    }

    #endregion
}
