using System;

/// <summary>
/// États possibles d'une séance de thérapie miroir.
/// Chaque état représente une phase du workflow.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// Aucune séance en cours
    /// </summary>
    Idle,
    
    /// <summary>
    /// Attente de la sélection du patient (côté soignant)
    /// </summary>
    WaitingForPatientSelection,
    
    /// <summary>
    /// Préparation de la séance (chargement préférences, setup environnement)
    /// </summary>
    Preparing,
    
    /// <summary>
    /// Capture initiale : patient bouge les deux mains (10-20s)
    /// </summary>
    CaptureInitiale,
    
    /// <summary>
    /// Phase de pré-exercice : respiration guidée (si activé)
    /// </summary>
    PreExercice,
    
    /// <summary>
    /// Thérapie miroir active : exercice principal
    /// </summary>
    TherapieMiroir,
    
    /// <summary>
    /// Capture finale : patient rebouge les mains pour comparer
    /// </summary>
    CaptureFinale,
    
    /// <summary>
    /// Calcul et affichage des résultats
    /// </summary>
    Resultats,
    
    /// <summary>
    /// Séance terminée avec succès
    /// </summary>
    Completed,
    
    /// <summary>
    /// Séance interrompue (killSwitch ou erreur)
    /// </summary>
    Interrupted,
    
    /// <summary>
    /// Erreur pendant la séance
    /// </summary>
    Error
}

/// <summary>
/// Données d'une phase de séance
/// </summary>
[Serializable]
public class SessionPhaseData
{
    public SessionState phase;
    public float duration; // Durée de la phase en secondes
    public float progress; // Progression 0.0 à 1.0
    public string message; // Message à afficher
    
    public SessionPhaseData(SessionState phase, float duration = 0f, string message = "")
    {
        this.phase = phase;
        this.duration = duration;
        this.progress = 0f;
        this.message = message;
    }
}

/// <summary>
/// Configuration d'une séance
/// </summary>
[Serializable]
public class SessionConfig
{
    // Patient concerné
    public string num_dossier;
    public ReVerso.Data.CoteAffecte cote_affecte;
    
    // Durées des phases (en secondes)
    public float duree_capture_initiale = 15f;
    public float duree_capture_finale = 15f;
    public float duree_therapie_miroir = 600f; // 10 minutes par défaut
    
    // Préférences
    public bool active_pre_exercice = true;
    public ReVerso.Data.Environnement environnement;
    public ReVerso.Data.ApparenceGuide apparence_guide;
    
    // Métadonnées
    public string date_debut;
    
    public SessionConfig(ReVerso.Data.PatientProfile patient)
    {
        num_dossier = patient.num_dossier;
        cote_affecte = patient.profil_medical.cote_affecte;
        active_pre_exercice = patient.preferences.active_phase_relaxation;
        environnement = patient.preferences.environnement_favori;
        apparence_guide = patient.preferences.apparence_guide;
        date_debut = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    }
}

/// <summary>
/// Résultats d'une séance complète
/// </summary>
[Serializable]
public class SessionResults
{
    public string num_dossier;
    public float amplitude_initiale;
    public float amplitude_finale;
    public float progression_pourcent;
    public bool amelioration_detectee;
    public float duree_totale_secondes;
    public string date_session;
    
    public SessionResults()
    {
        date_session = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    }
}
