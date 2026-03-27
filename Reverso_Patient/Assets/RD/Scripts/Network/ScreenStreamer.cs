using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Capture le rendu de la caméra VR et l'envoie au PC soignant via le réseau.
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
    [SerializeField] private int captureWidth = 480;

    [Tooltip("Hauteur de la capture (pixels)")]
    [SerializeField] private int captureHeight = 360;

    [Tooltip("Qualité JPEG (1-100). Plus bas = plus petit fichier, moins de qualité.")]
    [Range(1, 100)]
    [SerializeField] private int jpegQuality = 25;

    [Tooltip("Images par seconde envoyées au PC (5-15 recommandé)")]
    [Range(1, 20)]
    [SerializeField] private int targetFPS = 6;

    [Tooltip("Taille max d'une image JPEG avant envoi (octets). Réduit les pertes réseau.")]
    [SerializeField] private int maxFrameBytes = 45000;

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

    public void StartStreaming()
    {
        if (isStreaming)
            return;

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

    public void StopStreaming()
    {
        if (!isStreaming)
            return;

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
        renderTexture = new RenderTexture(captureWidth, captureHeight, 16, RenderTextureFormat.RGB565);
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

    private IEnumerator StreamLoop()
    {
        var waitEndOfFrame = new WaitForEndOfFrame();
        float nextSendTime = 0f;

        while (isStreaming)
        {
            yield return waitEndOfFrame;

            if (Time.unscaledTime < nextSendTime)
                continue;

            if (headsetServer == null || !headsetServer.HasConnectedClient)
                continue;

            nextSendTime = Time.unscaledTime + sendInterval;
            CaptureAndSendFrame();
        }
    }

    private void CaptureAndSendFrame()
    {
        if (targetCamera == null || renderTexture == null || readbackTexture == null)
            return;

        try
        {
            RenderTexture previousRT = targetCamera.targetTexture;
            targetCamera.targetTexture = renderTexture;
            targetCamera.Render();
            targetCamera.targetTexture = previousRT;

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            readbackTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            readbackTexture.Apply();
            RenderTexture.active = previousActive;

            byte[] jpegData = EncodeFrameWithBudget(readbackTexture);
            if (jpegData == null || jpegData.Length == 0)
                return;

            string base64 = Convert.ToBase64String(jpegData);
            headsetServer.SendToAllClients(new NetworkMessage(NetworkMessageType.VideoFrame, base64));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScreenStreamer] Erreur capture: {e.Message}");
        }
    }

    private byte[] EncodeFrameWithBudget(Texture2D source)
    {
        int quality = Mathf.Clamp(jpegQuality, 1, 100);
        byte[] data = ImageConversion.EncodeToJPG(source, quality);

        if (data == null || data.Length == 0)
            return data;

        if (data.Length <= maxFrameBytes)
            return data;

        // Fallback progressif: on réduit la qualité pour rester dans un budget réseau stable.
        int[] fallbackQualities = { 20, 15, 10, 7, 5 };
        for (int i = 0; i < fallbackQualities.Length; i++)
        {
            int q = Mathf.Min(quality, fallbackQualities[i]);
            data = ImageConversion.EncodeToJPG(source, q);
            if (data != null && data.Length > 0 && data.Length <= maxFrameBytes)
                return data;
        }

        // Si on dépasse encore, on envoie la meilleure tentative (évitons un black screen).
        return data;
    }
}
