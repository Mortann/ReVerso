using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ReVerso.Data
{
    /// <summary>
    /// Gestionnaire singleton pour la gestion des données patients.
    /// Fournit des méthodes CRUD (Create, Read, Update, Delete) et gère la persistance en JSON.
    /// 
    /// UTILISATION:
    /// - Accessible depuis n'importe quel script via PatientDataManager.Instance
    /// - Sauvegarde automatique après chaque modification
    /// - Les données sont stockées dans StreamingAssets/ReVerso/patients_data.json
    /// </summary>
    public class PatientDataManager : MonoBehaviour
    {
        #region Singleton

        private static PatientDataManager _instance;
        public static PatientDataManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PatientDataManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("PatientDataManager");
                        _instance = go.AddComponent<PatientDataManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Fields

        [Header("Configuration")]
        [Tooltip("Sauvegarder automatiquement après chaque modification")]
        [SerializeField] private bool autoSave = true;
        
        [Tooltip("Dossier de sauvegarde relatif à Application.streamingAssetsPath")]
        [SerializeField] private string saveFolderName = "ReVerso";
        
        [Tooltip("Nom du fichier de base de données")]
        [SerializeField] private string databaseFileName = "patients_data.json";

        [Header("État")]
        [SerializeField] private PatientProfile currentPatient;
        [SerializeField] private int totalPatients = 0;

        private PatientDatabase database;
        private string savePath;

        #endregion

        #region Events

        /// <summary>
        /// Événement déclenché quand un patient est créé
        /// </summary>
        public event Action<PatientProfile> OnPatientCreated;
        
        /// <summary>
        /// Événement déclenché quand un patient est modifié
        /// </summary>
        public event Action<PatientProfile> OnPatientUpdated;
        
        /// <summary>
        /// Événement déclenché quand un patient est supprimé
        /// </summary>
        public event Action<string> OnPatientDeleted;
        
        /// <summary>
        /// Événement déclenché quand une séance est ajoutée
        /// </summary>
        public event Action<PatientProfile, SessionData> OnSessionAdded;

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

            InitializeSavePath();
            LoadFromFile();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initialise le chemin de sauvegarde
        /// </summary>
        private void InitializeSavePath()
        {
            string folder = Path.Combine(Application.streamingAssetsPath, saveFolderName);
            
            // Créer le dossier s'il n'existe pas
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Debug.Log($"[PatientDataManager] Dossier créé: {folder}");
            }

            savePath = Path.Combine(folder, databaseFileName);
            Debug.Log($"[PatientDataManager] Chemin de sauvegarde: {savePath}");
        }

        #endregion

        #region CRUD - Create

        /// <summary>
        /// Crée un nouveau patient avec les informations de base
        /// </summary>
        /// <param name="numDossier">Numéro de dossier unique</param>
        /// <param name="nom">Nom de famille</param>
        /// <param name="prenom">Prénom</param>
        /// <param name="dateNaissance">Date de naissance (format: yyyy-MM-dd)</param>
        /// <returns>Le profil du patient créé, ou null si le numéro existe déjà</returns>
        public PatientProfile CreatePatient(string numDossier, string nom, string prenom, string dateNaissance)
        {
            // Vérifier si le patient existe déjà
            if (GetPatient(numDossier) != null)
            {
                Debug.LogWarning($"[PatientDataManager] Un patient avec le numéro {numDossier} existe déjà !");
                return null;
            }

            // Créer le nouveau patient
            PatientProfile newPatient = new PatientProfile(numDossier)
            {
                infos_personnelles = new InfosPersonnelles
                {
                    nom = nom,
                    prenom = prenom,
                    date_naissance = dateNaissance
                }
            };

            database.patients.Add(newPatient);
            totalPatients = database.patients.Count;
            
            Debug.Log($"[PatientDataManager] Patient créé: {newPatient.infos_personnelles.NomComplet} ({numDossier})");
            
            OnPatientCreated?.Invoke(newPatient);
            
            if (autoSave) SaveToFile();
            
            return newPatient;
        }

        /// <summary>
        /// Crée un nouveau patient avec un profil complet
        /// </summary>
        public PatientProfile CreatePatient(PatientProfile patient)
        {
            if (GetPatient(patient.num_dossier) != null)
            {
                Debug.LogWarning($"[PatientDataManager] Un patient avec le numéro {patient.num_dossier} existe déjà !");
                return null;
            }

            database.patients.Add(patient);
            totalPatients = database.patients.Count;
            
            OnPatientCreated?.Invoke(patient);
            
            if (autoSave) SaveToFile();
            
            return patient;
        }

        #endregion

        #region CRUD - Read

        /// <summary>
        /// Récupère un patient par son numéro de dossier
        /// </summary>
        public PatientProfile GetPatient(string numDossier)
        {
            return database.patients.FirstOrDefault(p => p.num_dossier == numDossier);
        }

        /// <summary>
        /// Récupère tous les patients
        /// </summary>
        public List<PatientProfile> GetAllPatients()
        {
            return database.patients;
        }

        /// <summary>
        /// Récupère le nombre total de patients
        /// </summary>
        public int GetPatientCount()
        {
            return database.patients.Count;
        }

        /// <summary>
        /// Vérifie si un patient existe
        /// </summary>
        public bool PatientExists(string numDossier)
        {
            return GetPatient(numDossier) != null;
        }

        #endregion

        #region CRUD - Update

        /// <summary>
        /// Met à jour les informations d'un patient
        /// </summary>
        public void UpdatePatient(PatientProfile updatedPatient)
        {
            PatientProfile existing = GetPatient(updatedPatient.num_dossier);
            if (existing == null)
            {
                Debug.LogWarning($"[PatientDataManager] Patient {updatedPatient.num_dossier} introuvable !");
                return;
            }

            // Remplacer l'ancien profil par le nouveau
            int index = database.patients.IndexOf(existing);
            database.patients[index] = updatedPatient;
            
            Debug.Log($"[PatientDataManager] Patient {updatedPatient.num_dossier} mis à jour");
            
            OnPatientUpdated?.Invoke(updatedPatient);
            
            if (autoSave) SaveToFile();
        }

        /// <summary>
        /// Met à jour les informations personnelles d'un patient
        /// </summary>
        public void UpdateInfosPersonnelles(string numDossier, InfosPersonnelles infos)
        {
            PatientProfile patient = GetPatient(numDossier);
            if (patient != null)
            {
                patient.infos_personnelles = infos;
                OnPatientUpdated?.Invoke(patient);
                if (autoSave) SaveToFile();
            }
        }

        /// <summary>
        /// Met à jour le profil médical d'un patient
        /// </summary>
        public void UpdateProfilMedical(string numDossier, ProfilMedical profil)
        {
            PatientProfile patient = GetPatient(numDossier);
            if (patient != null)
            {
                patient.profil_medical = profil;
                OnPatientUpdated?.Invoke(patient);
                if (autoSave) SaveToFile();
            }
        }

        /// <summary>
        /// Met à jour les préférences d'un patient
        /// </summary>
        public void UpdatePreferences(string numDossier, Preferences preferences)
        {
            PatientProfile patient = GetPatient(numDossier);
            if (patient != null)
            {
                patient.preferences = preferences;
                OnPatientUpdated?.Invoke(patient);
                if (autoSave) SaveToFile();
            }
        }

        #endregion

        #region CRUD - Delete

        /// <summary>
        /// Supprime un patient
        /// </summary>
        /// <returns>True si le patient a été supprimé, False sinon</returns>
        public bool DeletePatient(string numDossier)
        {
            PatientProfile patient = GetPatient(numDossier);
            if (patient == null)
            {
                Debug.LogWarning($"[PatientDataManager] Patient {numDossier} introuvable !");
                return false;
            }

            database.patients.Remove(patient);
            totalPatients = database.patients.Count;
            
            // Désélectionner si c'était le patient actif
            if (currentPatient?.num_dossier == numDossier)
            {
                currentPatient = null;
            }
            
            Debug.Log($"[PatientDataManager] Patient {numDossier} supprimé");
            
            OnPatientDeleted?.Invoke(numDossier);
            
            if (autoSave) SaveToFile();
            
            return true;
        }

        #endregion

        #region Session Management

        /// <summary>
        /// Ajoute une nouvelle séance pour un patient
        /// </summary>
        public SessionData AddSession(string numDossier, Metriques metriques)
        {
            PatientProfile patient = GetPatient(numDossier);
            if (patient == null)
            {
                Debug.LogWarning($"[PatientDataManager] Patient {numDossier} introuvable !");
                return null;
            }

            // Créer la nouvelle séance avec un ID incrémenté
            int nextId = patient.data.Count > 0 ? patient.data.Max(s => s.id_seance) + 1 : 1;
            SessionData newSession = new SessionData(nextId)
            {
                metriques = metriques
            };

            patient.data.Add(newSession);
            
            Debug.Log($"[PatientDataManager] Séance {nextId} ajoutée pour {patient.infos_personnelles.NomComplet} - Progression: {newSession.ProgressionPourcent:F1}%");
            
            OnSessionAdded?.Invoke(patient, newSession);
            
            if (autoSave) SaveToFile();
            
            return newSession;
        }

        /// <summary>
        /// Récupère toutes les séances d'un patient
        /// </summary>
        public List<SessionData> GetAllSessions(string numDossier)
        {
            PatientProfile patient = GetPatient(numDossier);
            return patient?.data ?? new List<SessionData>();
        }

        /// <summary>
        /// Récupère la dernière séance d'un patient
        /// </summary>
        public SessionData GetLastSession(string numDossier)
        {
            PatientProfile patient = GetPatient(numDossier);
            return patient?.data.LastOrDefault();
        }

        /// <summary>
        /// Récupère le nombre de séances d'un patient
        /// </summary>
        public int GetSessionCount(string numDossier)
        {
            PatientProfile patient = GetPatient(numDossier);
            return patient?.data.Count ?? 0;
        }

        #endregion

        #region Current Patient

        /// <summary>
        /// Définit le patient actuel (pour la séance en cours)
        /// </summary>
        public void SetCurrentPatient(string numDossier)
        {
            currentPatient = GetPatient(numDossier);
            if (currentPatient != null)
            {
                Debug.Log($"[PatientDataManager] Patient actuel: {currentPatient.infos_personnelles.NomComplet}");
            }
        }

        /// <summary>
        /// Récupère le patient actuel
        /// </summary>
        public PatientProfile GetCurrentPatient()
        {
            return currentPatient;
        }

        /// <summary>
        /// Vérifie si un patient est sélectionné
        /// </summary>
        public bool HasCurrentPatient()
        {
            return currentPatient != null;
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Sauvegarde la base de données en JSON
        /// </summary>
        public void SaveToFile()
        {
            try
            {
                database.UpdateTimestamp();
                string json = JsonUtility.ToJson(database, true);
                File.WriteAllText(savePath, json);
                Debug.Log($"[PatientDataManager] ✅ Sauvegarde réussie: {totalPatients} patient(s)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PatientDataManager] ❌ Erreur sauvegarde: {e.Message}");
            }
        }

        /// <summary>
        /// Charge la base de données depuis le JSON
        /// </summary>
        public void LoadFromFile()
        {
            if (!File.Exists(savePath))
            {
                Debug.LogWarning($"[PatientDataManager] Aucune sauvegarde trouvée, création d'une nouvelle base");
                database = new PatientDatabase();
                SaveToFile(); // Créer le fichier vide
                return;
            }

            try
            {
                string json = File.ReadAllText(savePath);
                database = JsonUtility.FromJson<PatientDatabase>(json);
                totalPatients = database.patients.Count;
                Debug.Log($"[PatientDataManager] ✅ Chargement réussi: {totalPatients} patient(s)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PatientDataManager] ❌ Erreur chargement: {e.Message}");
                database = new PatientDatabase();
            }
        }

        /// <summary>
        /// Réinitialise la base de données (DANGER : supprime tout !)
        /// </summary>
        public void ClearDatabase()
        {
            database = new PatientDatabase();
            currentPatient = null;
            totalPatients = 0;
            SaveToFile();
            Debug.LogWarning("[PatientDataManager] ⚠️ Base de données réinitialisée");
        }

        #endregion
    }
}
