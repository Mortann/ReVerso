using System;
using System.Collections.Generic;
using UnityEngine;
using ReVerso.Data;

/// <summary>
/// Collecteur de métriques côté SOIGNANT (PC).
/// Reçoit les données de métriques du casque via le réseau.
/// Ne fait PAS de hand tracking local — les calculs sont faits côté Patient.
/// 
/// FONCTIONNEMENT:
/// 1. S'abonne aux messages réseau du SoignantClient
/// 2. Reçoit les amplitudes calculées par le casque
/// 3. Stocke les données pour le PatientDataManager
/// </summary>
public class SessionMetricsCollector : MonoBehaviour
{
    [Header("Réseau")]
    [Tooltip("Référence au client réseau")]
    [SerializeField] private SoignantClient soignantClient;

    [Header("État actuel")]
    [SerializeField] private float lastReceivedAmplitude = 0f;
    [SerializeField] private int totalSamplesReceived = 0;
    [SerializeField] private bool isSessionActive = false;

    /// <summary>
    /// Événement déclenché quand une nouvelle amplitude est reçue
    /// </summary>
    public event Action<float> OnAmplitudeReceived;

    /// <summary>
    /// Événement déclenché quand les métriques finales sont reçues
    /// </summary>
    public event Action<Metriques> OnMetriquesReceived;

    private List<float> receivedAmplitudes = new List<float>();

    private void Start()
    {
        if (soignantClient == null)
            soignantClient = FindFirstObjectByType<SoignantClient>();

        if (soignantClient != null)
        {
            soignantClient.OnMessageReceived += OnNetworkMessage;
            Debug.Log("[SessionMetricsCollector] ✅ Prêt à recevoir les métriques du casque");
        }
        else
        {
            Debug.LogWarning("[SessionMetricsCollector] SoignantClient non trouvé !");
        }
    }

    private void OnDestroy()
    {
        if (soignantClient != null)
            soignantClient.OnMessageReceived -= OnNetworkMessage;
    }

    /// <summary>
    /// Traite les messages réseau contenant des métriques
    /// </summary>
    private void OnNetworkMessage(NetworkMessage message)
    {
        switch (message.type)
        {
            case NetworkMessageType.HandTrackingData:
                ProcessHandTrackingData(message.data);
                break;

            case NetworkMessageType.ExerciseCompleted:
                ProcessExerciseCompleted(message.data);
                break;
        }
    }

    /// <summary>
    /// Traite les données de tracking reçues (amplitude en temps réel)
    /// </summary>
    private void ProcessHandTrackingData(string jsonData)
    {
        try
        {
            var data = JsonUtility.FromJson<HandAmplitudeData>(jsonData);
            lastReceivedAmplitude = data.amplitude;
            totalSamplesReceived++;

            if (isSessionActive)
            {
                receivedAmplitudes.Add(data.amplitude);
            }

            OnAmplitudeReceived?.Invoke(data.amplitude);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SessionMetricsCollector] Erreur parsing données: {e.Message}");
        }
    }

    /// <summary>
    /// Traite la fin d'un exercice avec les métriques finales
    /// </summary>
    private void ProcessExerciseCompleted(string jsonData)
    {
        try
        {
            var metriques = JsonUtility.FromJson<Metriques>(jsonData);
            OnMetriquesReceived?.Invoke(metriques);
            Debug.Log($"[SessionMetricsCollector] 📊 Métriques reçues — Initiale: {metriques.moy_amplitude_initiale:F3}, Finale: {metriques.moy_amplitude_finale:F3}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SessionMetricsCollector] Erreur parsing métriques: {e.Message}");
        }
    }

    #region Public API

    /// <summary>
    /// Démarre une session de collecte (les amplitudes reçues seront stockées)
    /// </summary>
    public void StartSession()
    {
        isSessionActive = true;
        receivedAmplitudes.Clear();
        totalSamplesReceived = 0;
        Debug.Log("[SessionMetricsCollector] ⏺️ Session de collecte démarrée");
    }

    /// <summary>
    /// Arrête la session et retourne la moyenne des amplitudes reçues
    /// </summary>
    public float StopSessionAndGetAverage()
    {
        isSessionActive = false;

        if (receivedAmplitudes.Count == 0)
        {
            Debug.LogWarning("[SessionMetricsCollector] ⚠️ Aucune donnée reçue pendant la session");
            return 0f;
        }

        float sum = 0f;
        foreach (float v in receivedAmplitudes)
            sum += v;
        float average = sum / receivedAmplitudes.Count;

        Debug.Log($"[SessionMetricsCollector] ⏹️ Session terminée — {receivedAmplitudes.Count} échantillons — Moyenne: {average:F3}");
        return average;
    }

    /// <summary>
    /// Dernière amplitude reçue du casque
    /// </summary>
    public float LastAmplitude => lastReceivedAmplitude;

    /// <summary>
    /// Nombre total d'échantillons reçus
    /// </summary>
    public int TotalSamples => totalSamplesReceived;

    /// <summary>
    /// Session en cours ?
    /// </summary>
    public bool IsSessionActive => isSessionActive;

    #endregion
}
