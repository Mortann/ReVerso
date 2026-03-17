using System;
using UnityEngine;
using ReVerso.Data;

/// <summary>
/// États possibles d'une séance de thérapie.
/// Représente les différentes phases du workflow.
/// </summary>
public enum SessionState
{
    /// <summary>Aucune séance active</summary>
    Idle,
    
    /// <summary>En attente de la sélection du patient par le soignant</summary>
    WaitingForPatientSelection,
    
    /// <summary>Préparation de la séance (chargement, configuration)</summary>
    Preparing,
    
    /// <summary>Capture initiale des amplitudes (10-20s)</summary>
    CaptureInitiale,
    
    /// <summary>Exercices de pré-exo (respiration guidée)</summary>
    PreExercice,
    
    /// <summary>Phase de thérapie miroir</summary>
    TherapieMiroir,
    
    /// <summary>Capture finale des amplitudes (10-20s)</summary>
    CaptureFinale,
    
    /// <summary>Affichage et calcul des résultats</summary>
    Resultats,
    
    /// <summary>Séance terminée avec succès</summary>
    Completed,
    
    /// <summary>Séance interrompue (killSwitch ou erreur)</summary>
    Interrupted,
    
    /// <summary>Erreur bloquante</summary>
    Error
}

/// <summary>
/// Données de configuration pour une séance sur le Quest.
/// Ces données sont envoyées par le PC au début de la séance.
/// </summary>
[Serializable]
public class SessionConfig
{
    // Identification patient
    public string num_dossier;
    public string nom_complet;
    
    // Paramètres médicaux
    public CoteAffecte cote_affecte;
    
    // Préférences
    public Environnement environnement;
    public ApparenceGuide apparence_guide;

    // Timing des phases (en secondes)
    public float duree_capture_initiale = 15f;
    public float duree_pre_exercice = 60f;
    public float duree_therapie_miroir = 300f; // 5 minutes
    public float duree_capture_finale = 15f;

    // Options
    public bool active_pre_exercice = true;
    public bool afficher_guide_virtuel = true;

    // Métadonnées
    public string date_debut;

    /// <summary>
    /// Constructeur par défaut
    /// </summary>
    public SessionConfig() { }

    /// <summary>
    /// Constructeur depuis un profil patient
    /// </summary>
    public SessionConfig(PatientProfile patient)
    {
        if (patient == null)
            throw new ArgumentNullException(nameof(patient));

        num_dossier = patient.num_dossier;
        nom_complet = patient.infos_personnelles.NomComplet;
        cote_affecte = patient.profil_medical.cote_affecte;
        environnement = patient.preferences.environnement_favori;
        apparence_guide = patient.preferences.apparence_guide;
        active_pre_exercice = patient.preferences.active_phase_relaxation;
        date_debut = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        
        // Utiliser les valeurs par défaut pour les durées
    }
}

/// <summary>
/// Données d'une phase de séance en cours
/// </summary>
[Serializable]
public class SessionPhaseData
{
    public SessionState phase;
    public float duration;
    public float progress; // 0.0 à 1.0
    public string message;

    public SessionPhaseData(SessionState phase, float duration = 0f, string message = "")
    {
        this.phase = phase;
        this.duration = duration;
        this.progress = 0f;
        this.message = message;
    }
}

/// <summary>
/// Résultats d'une séance terminée.
/// Ces données sont renvoyées au PC pour sauvegarde.
/// </summary>
[Serializable]
public class SessionResults
{
    // Identification
    public string num_dossier;
    public string date_session; // Format DateTime
    
    // Métriques principales
    public float amplitude_initiale;
    public float amplitude_finale;
    
    // Statistiques calculées
    public float progression_pourcent;
    public bool amelioration_detectee;
    
    // Durées
    public float duree_capture_initiale;
    public float duree_pre_exercice;
    public float duree_therapie_miroir;
    public float duree_capture_finale;
    public float duree_totale_secondes;
    
    // Conditions
    public CoteAffecte cote_affecte;
    public Environnement environnement_utilise;
    public ApparenceGuide guide_utilise;
    
    // Métadonnées
    public bool session_complete; // false si interrompue
    public string raison_interruption; // Si session_complete = false

    /// <summary>
    /// Constructeur par défaut
    /// </summary>
    public SessionResults() { }

    /// <summary>
    /// Constructeur avec données de base
    /// </summary>
    public SessionResults(string numDossier, float ampInit, float ampFinal)
    {
        num_dossier = numDossier;
        date_session = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        amplitude_initiale = ampInit;
        amplitude_finale = ampFinal;
        
        // Calculer la progression
        CalculateProgression();
    }

    /// <summary>
    /// Calcule le pourcentage de progression
    /// </summary>
    public void CalculateProgression()
    {
        if (amplitude_initiale > 0)
        {
            float delta = amplitude_finale - amplitude_initiale;
            progression_pourcent = (delta / amplitude_initiale) * 100f;
            amelioration_detectee = delta > 0.01f; // Amélioration si > 1cm
        }
        else
        {
            progression_pourcent = 0f;
            amelioration_detectee = false;
        }
    }
}
