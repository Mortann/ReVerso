using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Capture le rendu de la caméra VR et l'envoie au PC soignant via le réseau.
/// Les frames sont encodées en JPEG (basse qualité pour le réseau),
/// converties en base64, puis envoyées comme NetworkMessage.
///
/// UTILISATION :
/// - Placé côté Quest (Patient)
/// - S'active/désactive via StartStreaming()/StopStreaming()
/// - Le PC reçoit les frames de type VideoFrame et les affiche dans un RawImage
/// </summary>
public class ScreenStreamer : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("Caméra dont on capture le rendu (XR Camera)")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("Serveur réseau pour envoyer les frames")]
    [SerializeField] private HeadsetServer headsetServer;

    [Header("Configuration")]
    [Tooltip("Largeur de la capture (pixels). Plus petit = moins de bande passante.")]
    [SerializeField] private int captureWidth = 640;

    [Tooltip("Hauteur de la capture (pixels)")]
    [SerializeField] private int captureHeight = 480;

    [Tooltip("Qualité JPEG (1-100). Plus bas = plus petit fichier, moins de qualité.")]
    [Range(1, 100)]
    [SerializeField] private int jpegQuality = 30;

    [Tooltip("Images par seconde envoyées au PC (5-15 recommandé)")]
    [Range(1, 30)]
    [SerializeField] private int targetFPS = 8;

    [Header("État")]
    [SerializeField] private bool isStreaming = false;

    public bool IsStreaming => isStreaming;

    private RenderTexture renderTexture;
    private Texture2D readbackTexture;
    private Coroutine streamCoroutine;
    private float sendInterval;

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (headsetServer == null)
            headsetServer = FindFirstObjectByType<HeadsetServer>();
    }

    private void OnDestroy()
    {
        StopStreaming();
        CleanupTextures();
    }

    /// <summary>
    /// Démarre le streaming vidéo vers le PC.
    /// </summary>
    public void StartStreaming()
    {
        if (isStreaming) return;

        if (targetCamera == null)
        {
            Debug.LogError("[ScreenStreamer] Aucune caméra assignée !");
            return;
        }

        if (headsetServer == null || !headsetServer.HasConnectedClient)
        {
            Debug.LogWarning("[ScreenStreamer] Pas de client PC connecté.");
            return;
        }

        InitTextures();
        sendInterval = 1f / targetFPS;
        isStreaming = true;
        streamCoroutine = StartCoroutine(StreamLoop());

        Debug.Log($"[ScreenStreamer] Streaming démarré ({captureWidth}x{captureHeight} @ {targetFPS}fps, JPEG Q{jpegQuality})");
        HeadsetServer.AddNetworkLog("📹 Streaming vidéo démarré");
    }

    /// <summary>
    /// Arrête le streaming vidéo.
    /// </summary>
    public void StopStreaming()
    {
        if (!isStreaming) return;

        isStreaming = false;
        if (streamCoroutine != null)
        {
            StopCoroutine(streamCoroutine);
            streamCoroutine = null;
        }

        CleanupTextures();

        Debug.Log("[ScreenStreamer] Streaming arrêté");
        HeadsetServer.AddNetworkLog("📹 Streaming vidéo arrêté");
    }

    private void InitTextures()
    {
        CleanupTextures();
        renderTexture = new RenderTexture(captureWidth, captureHeight, 24);
        readbackTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
    }

    private void CleanupTextures()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }

        if (readbackTexture != null)
        {
            Destroy(readbackTexture);
            readbackTexture = null;
        }
    }

    /// <summary>
    /// Boucle de capture et envoi des frames.
    /// Utilise WaitForEndOfFrame pour capturer après le rendu.
    /// </summary>
    private IEnumerator StreamLoop()
    {
        var waitEndOfFrame = new WaitForEndOfFrame();
        float nextSendTime = 0f;

        while (isStreaming)
        {
            yield return waitEndOfFrame;

            if (Time.unscaledTime < nextSendTime)
                continue;

            nextSendTime = Time.unscaledTime + sendInterval;

            if (headsetServer == null || !headsetServer.HasConnectedClient)
                continue;

            CaptureAndSendFrame();
        }
    }

    /// <summary>
    /// Capture un frame de la caméra, encode en JPEG, envoie au PC.
    /// </summary>
    private void CaptureAndSendFrame()
    {
        if (targetCamera == null || renderTexture == null) return;

        try
        {
            // Sauvegarder et remplacer le RenderTexture de la caméra
            RenderTexture previousRT = targetCamera.targetTexture;
            targetCamera.targetTexture = renderTexture;
            targetCamera.Render();
            targetCamera.targetTexture = previousRT;

            // Lire les pixels du RenderTexture
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            readbackTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            readbackTexture.Apply();
            RenderTexture.active = previousActive;

            // Encoder en JPEG
            byte[] jpegData = ImageConversion.EncodeToJPG(readbackTexture, jpegQuality);

            // Convertir en base64 et envoyer
            string base64 = Convert.ToBase64String(jpegData);
            headsetServer.SendToAllClients(
                new NetworkMessage(NetworkMessageType.VideoFrame, base64));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScreenStreamer] Erreur capture: {e.Message}");
        }
    }
}
