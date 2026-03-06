using UnityEngine;

/// <summary>
/// Contrôleur du Passthrough Meta Quest.
/// Permet d'activer/désactiver la vue caméra IRL dans le casque.
/// 
/// PRÉREQUIS:
/// 1. OVRManager doit être dans la scène avec "Passthrough Support" = Supported ou Required
/// 2. Un OVRPassthroughLayer doit être sur un GameObject
/// 3. La caméra doit pouvoir afficher un fond transparent
/// </summary>
public class PassthroughController : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("La caméra principale (XR Camera)")]
    [SerializeField] private Camera xrCamera;
    
    [Tooltip("Le composant OVRPassthroughLayer à activer/désactiver")]
    [SerializeField] private OVRPassthroughLayer ovrPassthroughLayer;
    
    [Tooltip("Couleur de fond quand le passthrough est actif (doit avoir alpha = 0)")]
    [SerializeField] private Color passthroughBackgroundColor = new Color(0, 0, 0, 0);
    
    [Tooltip("Couleur de fond normale (quand VR active)")]
    [SerializeField] private Color normalBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);

    [Header("Objets à cacher pendant le passthrough")]
    [Tooltip("GameObjects à désactiver quand le passthrough est actif (environnement VR, skybox, etc.)")]
    [SerializeField] private GameObject[] objectsToHideDuringPassthrough;

    [Header("Contrôle manette")]
    [Tooltip("Permettre au patient de basculer le passthrough avec le bouton A de la manette droite")]
    [SerializeField] private bool enableControllerToggle = true;

    [Header("État")]
    [SerializeField] private bool isPassthroughActive = false;

    /// <summary>
    /// Est-ce que le passthrough est actuellement actif ?
    /// </summary>
    public bool IsPassthroughActive => isPassthroughActive;

    /// <summary>
    /// Est-ce que le Passthrough est disponible ? (false en éditeur sans Quest Link)
    /// </summary>
    public bool IsPassthroughAvailable { get; private set; } = false;

    private void Start()
    {
        // Trouver la caméra automatiquement si non assignée
        if (xrCamera == null)
        {
            xrCamera = Camera.main;
        }

        // Vérifier la disponibilité du Passthrough
        CheckPassthroughAvailability();

        // S'assurer que le passthrough est désactivé au démarrage
        if (IsPassthroughAvailable)
        {
            SetPassthroughActive(false);
        }
    }

    private void Update()
    {
        // Bouton A de la manette droite pour basculer passthrough / monde virtuel
        if (enableControllerToggle 
            && OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            TogglePassthrough();
        }
    }

    /// <summary>
    /// Vérifie si le Passthrough est disponible (Quest Link connecté ou build sur casque)
    /// </summary>
    private void CheckPassthroughAvailability()
    {
        if (OVRManager.instance == null)
        {
            Debug.LogWarning("[PassthroughController] OVRManager non trouvé ! Ajoutez-le à votre scène.");
            IsPassthroughAvailable = false;
            return;
        }

        // Vérifier si le Passthrough est initialisé correctement
        IsPassthroughAvailable = OVRManager.instance.isInsightPassthroughEnabled;

        if (IsPassthroughAvailable)
        {
            Debug.Log("[PassthroughController] ✅ Passthrough disponible et initialisé.");
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[PassthroughController] ⚠️ Passthrough non disponible en éditeur.\n" +
                "C'est NORMAL si tu n'as pas Quest Link connecté.\n" +
                "Le Passthrough fonctionnera: \n" +
                "  - Avec Quest Link connecté au casque\n" +
                "  - Sur un build APK sur le casque");
            #else
            Debug.LogError("[PassthroughController] ❌ Passthrough non disponible sur le casque. Vérifiez les paramètres OVRManager.");
            #endif
        }
    }

    /// <summary>
    /// Active ou désactive le passthrough (vue caméra IRL)
    /// </summary>
    /// <param name="active">True pour activer le passthrough, False pour revenir en VR</param>
    public void SetPassthroughActive(bool active)
    {
        // Vérifier si le Passthrough est disponible
        if (!IsPassthroughAvailable && active)
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[PassthroughController] ⚠️ Passthrough demandé mais non disponible (normal en éditeur sans Quest Link)");
            #else
            Debug.LogError("[PassthroughController] ❌ Passthrough demandé mais non disponible !");
            #endif
            return;
        }

        isPassthroughActive = active;

        // Activer/désactiver le composant OVRPassthroughLayer directement
        if (ovrPassthroughLayer != null)
        {
            ovrPassthroughLayer.enabled = active;
            
            // S'assurer que le layer est bien visible
            if (active)
            {
                ovrPassthroughLayer.hidden = false;
            }
        }
        else
        {
            Debug.LogWarning("[PassthroughController] OVRPassthroughLayer non assigné !");
        }

        // Changer la couleur de fond de la caméra
        if (xrCamera != null)
        {
            if (active)
            {
                // Passthrough actif: fond transparent pour voir la caméra IRL
                xrCamera.clearFlags = CameraClearFlags.SolidColor;
                xrCamera.backgroundColor = passthroughBackgroundColor;
            }
            else
            {
                // VR active: fond normal
                xrCamera.clearFlags = CameraClearFlags.Skybox;
                xrCamera.backgroundColor = normalBackgroundColor;
            }
        }

        // Cacher/Afficher les objets VR
        foreach (var obj in objectsToHideDuringPassthrough)
        {
            if (obj != null)
            {
                obj.SetActive(!active);
            }
        }

        Debug.Log($"[PassthroughController] Passthrough {(active ? "ACTIVÉ - Vue IRL" : "DÉSACTIVÉ - Retour VR")}");
    }

    /// <summary>
    /// Bascule l'état du passthrough
    /// </summary>
    public void TogglePassthrough()
    {
        SetPassthroughActive(!isPassthroughActive);
    }
}
