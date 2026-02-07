using System.Collections.Generic;
using UnityEngine;
using ReVerso.Data;

/// <summary>
/// Collecteur de métriques pour mesurer l'amplitude de la main pendant une séance.
/// 
/// FONCTIONNEMENT:
/// 1. Démarre l'enregistrement avec StartRecording()
/// 2. Capture automatiquement l'amplitude de la main chaque frame
/// 3. Arrête et calcule la moyenne avec StopRecordingAndGetAverage()
/// 
/// CALCUL DE L'AMPLITUDE:
/// L'amplitude mesure l'ouverture de la main :
/// - Calcule la distance moyenne entre la paume et tous les doigts (5 fingertips)
/// - Normalise par rapport à une distance maximale de référence (0.25m = main très ouverte)
/// - Résultat: 0.0 (main fermée) à 1.0 (main complètement ouverte)
/// 
/// UTILISATION:
/// SessionMetricsCollector collector = GetComponent<SessionMetricsCollector>();
/// collector.StartRecording(CoteAffecte.Gauche, 30f); // Enregistrer 30 secondes
/// await Task.Delay(30000);
/// float amplitude = collector.StopRecordingAndGetAverage();
/// </summary>
public class SessionMetricsCollector : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("Handler pour lire les données de tracking des mains")]
    [SerializeField] private HandTrackingDebugger handTracker;

    [Header("Configuration")]
    [Tooltip("Distance maximale de référence (en mètres) pour normaliser l'amplitude")]
    [SerializeField] private float maxReferenceDistance = 0.25f; // 25cm = main très ouverte
    
    [Tooltip("Enregistrer seulement si la main est trackée")]
    [SerializeField] private bool requireTracking = true;

    [Header("État actuel")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private CoteAffecte coteEnCours;
    [SerializeField] private float recordingDuration = 0f;
    [SerializeField] private int samplesCollected = 0;

    // Données capturées
    private List<float> amplitudeSamples = new List<float>();
    private float recordingStartTime = 0f;
    private float recordingMaxDuration = 0f;

    #region Public API

    /// <summary>
    /// Démarre l'enregistrement de l'amplitude pour un côté donné
    /// </summary>
    /// <param name="coteAffecte">Côté de la main à mesurer (Gauche ou Droit)</param>
    /// <param name="durationSeconds">Durée d'enregistrement en secondes (0 = infini)</param>
    public void StartRecording(CoteAffecte coteAffecte, float durationSeconds = 30f)
    {
        if (handTracker == null)
        {
            Debug.LogError("[SessionMetricsCollector] HandTrackingDebugger non assigné !");
            return;
        }

        isRecording = true;
        coteEnCours = coteAffecte;
        recordingMaxDuration = durationSeconds;
        recordingStartTime = Time.time;
        recordingDuration = 0f;
        samplesCollected = 0;
        amplitudeSamples.Clear();

        Debug.Log($"[SessionMetricsCollector] ⏺️ Enregistrement démarré - Main {coteAffecte} - Durée: {durationSeconds}s");
    }

    /// <summary>
    /// Arrête l'enregistrement et retourne la moyenne des amplitudes capturées
    /// </summary>
    /// <returns>Amplitude moyenne (0.0 à 1.0), ou 0 si aucune donnée</returns>
    public float StopRecordingAndGetAverage()
    {
        isRecording = false;
        
        if (amplitudeSamples.Count == 0)
        {
            Debug.LogWarning("[SessionMetricsCollector] ⚠️ Aucune donnée capturée !");
            return 0f;
        }

        float average = CalculateAverage(amplitudeSamples);
        
        Debug.Log($"[SessionMetricsCollector] ⏹️ Enregistrement arrêté - {samplesCollected} échantillons - Moyenne: {average:F3}");
        
        return average;
    }

    /// <summary>
    /// Retourne les métriques moyennes calculées (pour PatientDataManager)
    /// </summary>
    public Metriques GetMetriques()
    {
        float average = StopRecordingAndGetAverage();
        return new Metriques
        {
            moy_amplitude_initiale = average,
            moy_amplitude_finale = average
        };
    }

    /// <summary>
    /// Vérifie si un enregistrement est en cours
    /// </summary>
    public bool IsRecording() => isRecording;

    /// <summary>
    /// Retourne le nombre d'échantillons collectés
    /// </summary>
    public int GetSampleCount() => amplitudeSamples.Count;

    /// <summary>
    /// Retourne la durée d'enregistrement écoulée (en secondes)
    /// </summary>
    public float GetRecordingDuration() => recordingDuration;

    /// <summary>
    /// Retourne le temps restant (si durée définie)
    /// </summary>
    public float GetRemainingTime()
    {
        if (recordingMaxDuration <= 0) return 0f;
        return Mathf.Max(0f, recordingMaxDuration - recordingDuration);
    }

    #endregion

    #region Unity Lifecycle

    private void Update()
    {
        if (!isRecording) return;

        // Mettre à jour la durée
        recordingDuration = Time.time - recordingStartTime;

        // Arrêter automatiquement si durée max atteinte
        if (recordingMaxDuration > 0 && recordingDuration >= recordingMaxDuration)
        {
            StopRecordingAndGetAverage();
            return;
        }

        // Capturer l'amplitude de la main
        CaptureHandAmplitude();
    }

    #endregion

    #region Capture Logic

    /// <summary>
    /// Capture l'amplitude actuelle de la main
    /// </summary>
    private void CaptureHandAmplitude()
    {
        if (handTracker == null) return;

        // Sélectionner la bonne main
        HandTrackingDebugger.HandData handData = (coteEnCours == CoteAffecte.Gauche)
            ? handTracker.LeftHandData
            : handTracker.RightHandData;

        // Vérifier si la main est trackée
        if (requireTracking && !handData.isTracked)
        {
            return; // Skip si la main n'est pas détectée
        }

        // Calculer l'amplitude
        float amplitude = CalculateHandAmplitude(handData);
        
        if (amplitude > 0f) // Ignorer les valeurs invalides
        {
            amplitudeSamples.Add(amplitude);
            samplesCollected++;
        }
    }

    /// <summary>
    /// Calcule l'amplitude de la main (ouverture) basée sur la distance Palm → Fingertips
    /// </summary>
    /// <param name="handData">Données de la main</param>
    /// <returns>Amplitude normalisée entre 0.0 (fermée) et 1.0 (ouverte)</returns>
    private float CalculateHandAmplitude(HandTrackingDebugger.HandData handData)
    {
        Vector3 palm = handData.palmPosition;

        // Vérifier que la paume est valide
        if (palm == Vector3.zero)
        {
            return 0f;
        }

        // Calculer la distance entre la paume et chaque fingertip
        float distThumb = Vector3.Distance(palm, handData.thumbTipPosition);
        float distIndex = Vector3.Distance(palm, handData.indexTipPosition);
        float distMiddle = Vector3.Distance(palm, handData.middleTipPosition);
        float distRing = Vector3.Distance(palm, handData.ringTipPosition);
        float distLittle = Vector3.Distance(palm, handData.littleTipPosition);

        // Calculer la moyenne des distances
        float averageDistance = (distThumb + distIndex + distMiddle + distRing + distLittle) / 5f;

        // Normaliser par rapport à la distance de référence
        float normalizedAmplitude = Mathf.Clamp01(averageDistance / maxReferenceDistance);

        return normalizedAmplitude;
    }

    /// <summary>
    /// Calcule la moyenne d'une liste de valeurs
    /// </summary>
    private float CalculateAverage(List<float> values)
    {
        if (values.Count == 0) return 0f;

        float sum = 0f;
        foreach (float value in values)
        {
            sum += value;
        }
        return sum / values.Count;
    }

    #endregion

    #region Debug Helpers

    /// <summary>
    /// Affiche les statistiques de l'enregistrement en cours
    /// </summary>
    public void LogCurrentStats()
    {
        if (!isRecording)
        {
            Debug.Log("[SessionMetricsCollector] Aucun enregistrement en cours");
            return;
        }

        float currentAverage = amplitudeSamples.Count > 0 ? CalculateAverage(amplitudeSamples) : 0f;
        
        Debug.Log($"[SessionMetricsCollector] Statistiques en cours:\n" +
                  $"  Durée: {recordingDuration:F1}s / {recordingMaxDuration}s\n" +
                  $"  Échantillons: {samplesCollected}\n" +
                  $"  Amplitude moyenne: {currentAverage:F3}\n" +
                  $"  Main: {coteEnCours}");
    }

    #endregion
}
