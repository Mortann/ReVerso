using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Structures de données pour la gestion des patients et de leurs séances.
/// Toutes les classes sont sérialisables pour permettre la sauvegarde en JSON.
/// </summary>
namespace ReVerso.Data
{
    #region Enums

    /// <summary>
    /// Côté du corps affecté par l'hémiplégie
    /// </summary>
    public enum CoteAffecte
    {
        Gauche,
        Droit
    }

    /// <summary>
    /// Environnements de thérapie disponibles
    /// </summary>
    public enum Environnement
    {
        Foret,
        Montagne,
        Interieur
    }

    /// <summary>
    /// Apparence du guide virtuel
    /// </summary>
    public enum ApparenceGuide
    {
        Feminin,
        Masculin
    }

    #endregion

    #region Base de données patients

    /// <summary>
    /// Conteneur racine pour tous les patients
    /// Note: List est utilisée au lieu de Dictionary car JsonUtility ne sérialise pas les Dictionary
    /// </summary>
    [Serializable]
    public class PatientDatabase
    {
        public string version = "1.0";
        public string derniere_modification;
        public List<PatientProfile> patients = new List<PatientProfile>();

        public PatientDatabase()
        {
            derniere_modification = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        /// <summary>
        /// Met à jour le timestamp de dernière modification
        /// </summary>
        public void UpdateTimestamp()
        {
            derniere_modification = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        }
    }

    #endregion

    #region Profil patient

    /// <summary>
    /// Profil complet d'un patient
    /// </summary>
    [Serializable]
    public class PatientProfile
    {
        public string num_dossier;
        public InfosPersonnelles infos_personnelles;
        public ProfilMedical profil_medical;
        public Preferences preferences;
        public List<SessionData> data;

        public PatientProfile(string numDossier)
        {
            num_dossier = numDossier;
            infos_personnelles = new InfosPersonnelles();
            profil_medical = new ProfilMedical();
            preferences = new Preferences();
            data = new List<SessionData>();
        }
    }

    /// <summary>
    /// Informations personnelles du patient
    /// </summary>
    [Serializable]
    public class InfosPersonnelles
    {
        public string nom;
        public string prenom;
        public string date_naissance; // Format: "yyyy-MM-dd"

        /// <summary>
        /// Retourne le nom complet (Prénom NOM)
        /// </summary>
        public string NomComplet => $"{prenom} {nom?.ToUpper()}";
    }

    /// <summary>
    /// Profil médical du patient
    /// </summary>
    [Serializable]
    public class ProfilMedical
    {
        public CoteAffecte cote_affecte;
        
        [Range(0f, 1f)]
        public float niveau_gravite; // 0.0 = léger, 1.0 = sévère
        
        public string pathologie_origine; // Ex: "AVC", "Traumatisme crânien"
        public string notes_medicales;

        /// <summary>
        /// Retourne une description textuelle du niveau de gravité
        /// </summary>
        public string GraviteDescription
        {
            get
            {
                if (niveau_gravite < 0.33f) return "Légère";
                if (niveau_gravite < 0.66f) return "Modérée";
                return "Sévère";
            }
        }
    }

    /// <summary>
    /// Préférences du patient pour la thérapie
    /// </summary>
    [Serializable]
    public class Preferences
    {
        public Environnement environnement_favori;
        public ApparenceGuide apparence_guide;
        public bool active_phase_relaxation;

        public Preferences()
        {
            environnement_favori = Environnement.Foret;
            apparence_guide = ApparenceGuide.Feminin;
            active_phase_relaxation = true;
        }
    }

    #endregion

    #region Données de séance

    /// <summary>
    /// Données d'une séance de thérapie
    /// </summary>
    [Serializable]
    public class SessionData
    {
        public int id_seance;
        public string date_seance; // Format: "yyyy-MM-ddTHH:mm:ss"
        public Metriques metriques;

        public SessionData(int idSeance)
        {
            id_seance = idSeance;
            date_seance = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            metriques = new Metriques();
        }

        /// <summary>
        /// Retourne la progression en pourcentage (0-100)
        /// </summary>
        public float ProgressionPourcent
        {
            get
            {
                if (metriques.moy_amplitude_initiale <= 0) return 0f;
                
                float progression = ((metriques.moy_amplitude_finale - metriques.moy_amplitude_initiale) 
                                     / metriques.moy_amplitude_initiale) * 100f;
                return Mathf.Round(progression * 10f) / 10f; // Arrondi à 1 décimale
            }
        }
    }

    /// <summary>
    /// Métriques de performance de la main pendant une séance
    /// </summary>
    [Serializable]
    public class Metriques
    {
        [Range(0f, 1f)]
        public float moy_amplitude_initiale; // Moyenne amplitude AVANT thérapie miroir
        
        [Range(0f, 1f)]
        public float moy_amplitude_finale;   // Moyenne amplitude APRÈS thérapie miroir

        /// <summary>
        /// Retourne true si une amélioration est détectée
        /// </summary>
        public bool AmeliorationDetectee => moy_amplitude_finale > moy_amplitude_initiale;

        /// <summary>
        /// Retourne la différence d'amplitude (peut être négative si régression)
        /// </summary>
        public float DifferenceAmplitude => moy_amplitude_finale - moy_amplitude_initiale;
    }

    #endregion
}
