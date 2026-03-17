using System;
using UnityEngine;
using ReVerso.Data;

/// <summary>
/// Contrôleur de séance côté SOIGNANT (PC).
/// Permet de démarrer/arrêter une séance et de suivre la progression.
/// 
/// FONCTIONNALITÉS :
/// - Démarrer une séance avec un patient sélectionné
/// - KillSwitch pour arrêter immédiatement
/// - Suivi de l'état et de la progression en temps réel
/// - Réception des résultats et sauvegarde automatique
/// 
/// COMMUNICATION RÉSEAU :
/// - Envoie : StartSession, StopSession
/// - Reçoit : SessionStateChanged, SessionProgress, SessionCompleted
/// </summary>
public class SessionControllerPC : MonoBehaviour
{
    #region Singleton

    private static SessionControllerPC _instance;
    public static SessionControllerPC Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SessionControllerPC>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("SessionControllerPC");
                    _instance = go.AddComponent<SessionControllerPC>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    #endregion

    #region Inspector Fields

    [Header("Références")]
    [Tooltip("Serveur réseau pour communiquer avec le Quest")]
    [SerializeField] private NetworkServer networkServer;

    [Header("État de la Séance Distante")]
    [SerializeField] private SessionState currentRemoteState = SessionState.Idle;
    [SerializeField] private float currentProgress = 0f;
    [SerializeField] private string currentPatientNumDossier = "";
    [SerializeField] private bool isSessionActive = false;

    [Header("Résultats")]
    [SerializeField] private SessionResults lastSessionResults;

    #endregion

    #region Events

    /// <summary>
    /// Événement déclenché quand l'état de la séance distante change
    /// </summary>
    public event Action<SessionState> OnRemoteStateChanged;
    
    /// <summary>
    /// Événement déclenché pour la progression de la phase actuelle
    /// </summary>
    public event Action<float> OnRemoteProgress;
    
    /// <summary>
    /// Événement déclenché quand la capture initiale est complète
    /// </summary>
    public event Action<float> OnCaptureInitialeComplete;
    
    /// <summary>
    /// Événement déclenché quand la capture finale est complète
    /// </summary>
    public event Action<float> OnCaptureFinaleComplete;
    
    /// <summary>
    /// Événement déclenché quand la séance est terminée
    /// </summary>
    public event Action<SessionResults> OnSessionCompleted;

    #endregion

    #region Properties

    public SessionState CurrentRemoteState => currentRemoteState;
    public float CurrentProgress => currentProgress;
    public bool IsSessionActive => isSessionActive;
    public SessionResults LastSessionResults => lastSessionResults;

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

        // Trouver le serveur réseau automatiquement
        if (networkServer == null)
            networkServer = FindFirstObjectByType<NetworkServer>();

        if (networkServer == null)
            Debug.LogError("[SessionControllerPC] NetworkServer non trouvé !");
    }

    private void OnEnable()
    {
        // S'abonner aux messages réseau du Quest
        if (networkServer != null)
        {
            networkServer.OnMessageReceived += OnNetworkMessageReceived;
        }
    }

    private void OnDisable()
    {
        if (networkServer != null)
        {
            networkServer.OnMessageReceived -= OnNetworkMessageReceived;
        }
    }

    #endregion

    #region Session Control

    /// <summary>
    /// Démarre une séance pour le patient actuellement sélectionné
    /// </summary>
    public void StartSession()
    {
        // Récupérer le patient actuel
        PatientProfile patient = PatientDataManager.Instance.GetCurrentPatient();

        if (patient == null)
        {
            Debug.LogError("[SessionControllerPC] Aucun patient sélectionné !");
            return;
        }

        StartSession(patient);
    }

    /// <summary>
    /// Démarre une séance pour un patient spécifique
    /// </summary>
    public void StartSession(PatientProfile patient)
    {
        if (isSessionActive)
        {
            Debug.LogWarning("[SessionControllerPC] Une séance est déjà en cours !");
            return;
        }

        if (networkServer == null || !networkServer.HasConnectedClient)
        {
            Debug.LogError("[SessionControllerPC] Aucun casque connecté !");
            return;
        }

        // Créer la configuration de séance
        SessionConfig config = new SessionConfig(patient);
        
        currentPatientNumDossier = patient.num_dossier;
        isSessionActive = true;
        currentRemoteState = SessionState.Preparing;

        Debug.Log($"[SessionControllerPC] 🎬 Démarrage séance pour {patient.infos_personnelles.NomComplet}");

        // Envoyer la commande au Quest
        string configJson = JsonUtility.ToJson(config);
        SendNetworkMessage(NetworkMessageType.StartSession, configJson);
    }

    /// <summary>
    /// Arrête immédiatement la séance en cours (KillSwitch)
    /// </summary>
    public void StopSession()
    {
        if (!isSessionActive)
        {
            Debug.LogWarning("[SessionControllerPC] Aucune séance en cours à arrêter");
            return;
        }

        Debug.LogWarning("[SessionControllerPC] ⛔ KillSwitch activé - Arrêt de la séance");

        // Envoyer la commande au Quest
        SendNetworkMessage(NetworkMessageType.StopSession);

        // Réinitialiser l'état local
        ResetSessionState();
    }

    /// <summary>
    /// Met la séance en pause (à implémenter)
    /// </summary>
    public void PauseSession()
    {
        if (!isSessionActive)
        {
            Debug.LogWarning("[SessionControllerPC] Aucune séance en cours");
            return;
        }

        Debug.Log("[SessionControllerPC] ⏸️ Mise en pause");
        SendNetworkMessage(NetworkMessageType.PauseSession);
    }

    /// <summary>
    /// Reprend la séance en pause (à implémenter)
    /// </summary>
    public void ResumeSession()
    {
        if (!isSessionActive)
        {
            Debug.LogWarning("[SessionControllerPC] Aucune séance en cours");
            return;
        }

        Debug.Log("[SessionControllerPC] ▶️ Reprise");
        SendNetworkMessage(NetworkMessageType.ResumeSession);
    }

    #endregion

    #region Network Communication

    /// <summary>
    /// Gestionnaire de messages réseau reçus du Quest
    /// </summary>
    private void OnNetworkMessageReceived(NetworkMessage message)
    {
        switch (message.type)
        {
            case NetworkMessageType.SessionStateChanged:
                // Le Quest a changé de phase
                if (Enum.TryParse(message.data, out SessionState newState))
                {
                    UpdateRemoteState(newState);
                }
                break;

            case NetworkMessageType.SessionProgress:
                // Progression dans la phase actuelle
                if (float.TryParse(message.data, out float progress))
                {
                    currentProgress = progress;
                    OnRemoteProgress?.Invoke(progress);
                }
                break;

            case NetworkMessageType.CaptureInitialeComplete:
                // Capture initiale terminée
                if (float.TryParse(message.data, out float amplitudeInitiale))
                {
                    Debug.Log($"[SessionControllerPC] 📊 Capture initiale : {amplitudeInitiale:F3}");
                    OnCaptureInitialeComplete?.Invoke(amplitudeInitiale);
                }
                break;

            case NetworkMessageType.CaptureFinaleComplete:
                // Capture finale terminée
                if (float.TryParse(message.data, out float amplitudeFinale))
                {
                    Debug.Log($"[SessionControllerPC] 📊 Capture finale : {amplitudeFinale:F3}");
                    OnCaptureFinaleComplete?.Invoke(amplitudeFinale);
                }
                break;

            case NetworkMessageType.SessionCompleted:
                // Séance terminée avec résultats
                HandleSessionCompleted(message.data);
                break;
        }
    }

    /// <summary>
    /// Envoie un message au Quest
    /// </summary>
    private void SendNetworkMessage(NetworkMessageType type, string data = "")
    {
        if (networkServer == null)
        {
            Debug.LogError("[SessionControllerPC] NetworkServer non trouvé !");
            return;
        }

        NetworkMessage message = new NetworkMessage(type, data);
        networkServer.SendToAllClients(message);
    }

    #endregion

    #region State Management

    /// <summary>
    /// Met à jour l'état de la séance distante
    /// </summary>
    private void UpdateRemoteState(SessionState newState)
    {
        if (currentRemoteState == newState) return;

        SessionState previousState = currentRemoteState;
        currentRemoteState = newState;

        Debug.Log($"[SessionControllerPC] Quest : {previousState} → {newState}");

        // Notifier
        OnRemoteStateChanged?.Invoke(newState);

        // Si la séance est terminée ou interrompue, réinitialiser
        if (newState == SessionState.Completed || newState == SessionState.Interrupted)
        {
            isSessionActive = false;
        }
    }

    /// <summary>
    /// Gère la réception des résultats de séance
    /// </summary>
    private void HandleSessionCompleted(string resultsJson)
    {
        try
        {
            lastSessionResults = JsonUtility.FromJson<SessionResults>(resultsJson);

            Debug.Log($"[SessionControllerPC] ✅ Séance terminée !\n" +
                      $"  Patient : {currentPatientNumDossier}\n" +
                      $"  Amplitude initiale : {lastSessionResults.amplitude_initiale:F3}\n" +
                      $"  Amplitude finale : {lastSessionResults.amplitude_finale:F3}\n" +
                      $"  Progression : {lastSessionResults.progression_pourcent:F1}%\n" +
                      $"  Amélioration : {(lastSessionResults.amelioration_detectee ? "✅" : "❌")}");

            // Enregistrer automatiquement dans la base de données
            SaveSessionResults();

            // Notifier
            OnSessionCompleted?.Invoke(lastSessionResults);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SessionControllerPC] Erreur parsing résultats : {e.Message}");
        }
    }

    /// <summary>
    /// Sauvegarde les résultats dans la base de données
    /// </summary>
    private void SaveSessionResults()
    {
        if (lastSessionResults == null)
        {
            Debug.LogWarning("[SessionControllerPC] Aucun résultat à sauvegarder");
            return;
        }

        // Créer les métriques
        Metriques metriques = new Metriques
        {
            moy_amplitude_initiale = lastSessionResults.amplitude_initiale,
            moy_amplitude_finale = lastSessionResults.amplitude_finale
        };

        // Ajouter la séance au patient
        SessionData session = PatientDataManager.Instance.AddSession(
            lastSessionResults.num_dossier, 
            metriques
        );

        if (session != null)
        {
            Debug.Log($"[SessionControllerPC] 💾 Séance enregistrée (ID: {session.id_seance})");
        }
        else
        {
            Debug.LogError("[SessionControllerPC] ❌ Échec de l'enregistrement de la séance");
        }
    }

    /// <summary>
    /// Réinitialise l'état de la séance
    /// </summary>
    private void ResetSessionState()
    {
        isSessionActive = false;
        currentRemoteState = SessionState.Idle;
        currentProgress = 0f;
        currentPatientNumDossier = "";
    }

    #endregion

    #region Public API

    /// <summary>
    /// Vérifie si un casque est connecté
    /// </summary>
    public bool IsHeadsetConnected()
    {
        return networkServer != null && networkServer.HasConnectedClient;
    }

    /// <summary>
    /// Retourne le nom de la phase actuelle
    /// </summary>
    public string GetCurrentPhaseName()
    {
        return currentRemoteState switch
        {
            SessionState.Idle => "En attente",
            SessionState.WaitingForPatientSelection => "Sélection patient",
            SessionState.Preparing => "Préparation",
            SessionState.CaptureInitiale => "Capture initiale",
            SessionState.PreExercice => "Exercices de respiration",
            SessionState.TherapieMiroir => "Thérapie miroir",
            SessionState.CaptureFinale => "Capture finale",
            SessionState.Resultats => "Calcul des résultats",
            SessionState.Completed => "Terminée",
            SessionState.Interrupted => "Interrompue",
            SessionState.Error => "Erreur",
            _ => "Inconnu"
        };
    }

    /// <summary>
    /// Retourne une description de la phase actuelle
    /// </summary>
    public string GetCurrentPhaseDescription()
    {
        return currentRemoteState switch
        {
            SessionState.CaptureInitiale => "Le patient essaie de bouger ses mains...",
            SessionState.PreExercice => "Exercices de respiration guidée",
            SessionState.TherapieMiroir => "Thérapie miroir en cours",
            SessionState.CaptureFinale => "Mesure finale en cours...",
            SessionState.Resultats => "Calcul de la progression",
            _ => ""
        };
    }

    /// <summary>
    /// Retourne les statistiques de la dernière séance
    /// </summary>
    public string GetLastSessionSummary()
    {
        if (lastSessionResults == null)
            return "Aucune séance enregistrée";

        return $"Dernière séance : {lastSessionResults.date_session}\n" +
               $"Progression : {lastSessionResults.progression_pourcent:F1}%\n" +
               $"Durée : {lastSessionResults.duree_totale_secondes / 60f:F1} min";
    }

    #endregion
}
