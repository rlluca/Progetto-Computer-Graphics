using System.Collections;
using System.Text;
using System.Threading.Tasks; 
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using GLTFast; 

public class ImageGeneratorClient : MonoBehaviour
{
    [Header("UI Elements - Immagine")]
    public TMP_InputField promptInput;
    public Button generateImageButton; 
    public RawImage resultImage;

    [Header("UI Elements - 3D")]
    public Button generate3DButton; 
    public Transform spawnPoint; // <--- Ecco la variabile che mancava!

    [Header("Impostazioni Server")]
    public string serverUrlImage = "http://192.168.80.138:8081/generate-image";
    public string serverUrl3D = "http://192.168.80.138:8081/generate-3d"; 
    public string sessionId = "test";

    // Public così ImageEditorClient può leggerla e aggiornarla
    public string lastImagePath = ""; 
    // Variabile per tenere traccia del modello 3D attualmente nella scena
    private GameObject currentModelContainer = null;

    #region Classi Dati JSON
    [System.Serializable]
    private class GenerateRequest
    {
        public string session_id;
        public string prompt;
    }

    [System.Serializable]
    private class GenerateResponse
    {
        public string image_path;
        public string image_url;
        public string status;
    }

    [System.Serializable]
    private class Generate3DRequest
    {
        public string session_id;
        public string image_path;
    }

    [System.Serializable]
    private class Generate3DResponse
    {
        public string model3d_url; 
        public string model3d_path; 
        public string status;
    }
    #endregion

    void Start()
    {
        if (generateImageButton != null) generateImageButton.onClick.AddListener(OnGenerateImageClicked);
        
        if (generate3DButton != null)
        {
            generate3DButton.onClick.AddListener(OnGenerate3DClicked);
            generate3DButton.interactable = false; 
        }
    }

    #region Fase 1: Generazione Immagine (Z-Image Turbo)
    void OnGenerateImageClicked()
    {
        string prompt = promptInput.text.Trim();

        if (string.IsNullOrEmpty(prompt))
        {
            Debug.LogWarning("Prompt vuoto. Inserisci un testo.");
            return;
        }

        generateImageButton.interactable = false; 
        if (generate3DButton != null) generate3DButton.interactable = false;

        StartCoroutine(GenerateImage(prompt));
    }

    IEnumerator GenerateImage(string prompt)
    {
        GenerateRequest body = new GenerateRequest
        {
            session_id = sessionId,
            prompt = prompt
        };

        string json = JsonUtility.ToJson(body);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(serverUrlImage, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Errore POST Immagine: " + request.error);
                generateImageButton.interactable = true;
                yield break;
            }

            GenerateResponse response = JsonUtility.FromJson<GenerateResponse>(request.downloadHandler.text);

            if (response == null || response.status != "ok" || string.IsNullOrEmpty(response.image_url))
            {
                Debug.LogError("Risposta Immagine non valida: " + request.downloadHandler.text);
                generateImageButton.interactable = true;
                yield break;
            }

            lastImagePath = response.image_path;

            yield return StartCoroutine(DownloadAndShowImage(response.image_url));
            
            if (generate3DButton != null) generate3DButton.interactable = true;
        }

        generateImageButton.interactable = true;
    }

    IEnumerator DownloadAndShowImage(string imageUrl)
    {
        Debug.Log("Sto cercando di scaricare l'immagine da: " + imageUrl);
        
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Errore download immagine: " + request.error);
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            resultImage.texture = texture;
        }
    }
    #endregion

    #region Fase 2: Generazione Modello 3D (Trellis)
    void OnGenerate3DClicked()
    {
        if (string.IsNullOrEmpty(lastImagePath))
        {
            Debug.LogWarning("Nessuna immagine disponibile per generare il modello 3D.");
            return;
        }

        generate3DButton.interactable = false;
        StartCoroutine(Generate3DModel(lastImagePath));
    }

    IEnumerator Generate3DModel(string imagePath)
    {
        Generate3DRequest body = new Generate3DRequest
        {
            session_id = sessionId,
            image_path = imagePath
        };

        string json = JsonUtility.ToJson(body);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        Debug.Log("Richiesta a Trellis avviata. La generazione 3D richiede tempo, attendere...");

        using (UnityWebRequest request = new UnityWebRequest(serverUrl3D, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            request.timeout = 300; 

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Errore POST 3D: " + request.error);
                generate3DButton.interactable = true;
                yield break;
            }

            Generate3DResponse response = JsonUtility.FromJson<Generate3DResponse>(request.downloadHandler.text);

            if (response == null || response.status != "ok" || string.IsNullOrEmpty(response.model3d_url))
            {
                Debug.LogError("Risposta 3D non valida: " + request.downloadHandler.text);
                generate3DButton.interactable = true;
                yield break;
            }

            Debug.Log("Generazione 3D completata! Inizio il download del modello.");
            yield return StartCoroutine(DownloadAndSave3DModel(response.model3d_url));
        }

        generate3DButton.interactable = true;
    }

    IEnumerator DownloadAndSave3DModel(string modelUrl)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(modelUrl))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Errore download modello 3D: " + request.error);
                yield break;
            }

            string extension = modelUrl.EndsWith(".obj") ? ".obj" : ".glb";
            string savePath = Application.persistentDataPath + "/generated_model" + extension;
            System.IO.File.WriteAllBytes(savePath, request.downloadHandler.data);
            
            Debug.Log($"Modello salvato in: {savePath}");

            if (extension == ".glb")
            {
                LoadModelIntoScene(savePath);
            }
            else
            {
                Debug.LogWarning("Il file generato non è un .glb. glTFast supporta solo .glb o .gltf.");
            }
        }
    }
    #endregion

    #region Fase 3: Visualizzazione Modello (glTFast)
    async void LoadModelIntoScene(string filePath)
    {
        Debug.Log("Caricamento del modello nella scena in corso...");

        // --- NUOVA PARTE: ELIMINA IL VECCHIO MODELLO ---
        if (currentModelContainer != null)
        {
            Destroy(currentModelContainer); // Cancella il modello precedente dalla scena
        }

        // --- MODIFICA: Usiamo la nostra variabile invece di crearne una locale ---
        currentModelContainer = new GameObject("Trellis_Generated_Model");
        
        var gltfImport = new GltfImport();
        bool success = await gltfImport.Load($"file://{filePath}");
        
        if (success)
        {
            // Nota che qui passiamo currentModelContainer invece di modelContainer
            bool instantiateSuccess = await gltfImport.InstantiateMainSceneAsync(currentModelContainer.transform);
            
            if (instantiateSuccess)
            {
                // Calcoliamo quanto è grande il modello e dove si trova il suo punto più basso
                Renderer[] renderers = currentModelContainer.GetComponentsInChildren<Renderer>();
                
                if (renderers.Length > 0) 
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) 
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }

                    float offsetDalFondo = currentModelContainer.transform.position.y - bounds.min.y;

                    if (spawnPoint != null)
                    {
                        currentModelContainer.transform.position = new Vector3(
                            spawnPoint.position.x, 
                            spawnPoint.position.y + offsetDalFondo, 
                            spawnPoint.position.z
                        );
                        currentModelContainer.transform.rotation = spawnPoint.rotation;
                    }
                    else
                    {
                        currentModelContainer.transform.position = new Vector3(0, offsetDalFondo, 2f);
                    }
                }
                
                Debug.Log("Successo! Il modello 3D è ora visibile e appoggiato correttamente.");
            }
            else
            {
                Debug.LogError("Errore durante la generazione nella scena del .glb.");
                Destroy(currentModelContainer);
            }
        }
        else
        {
            Debug.LogError("Impossibile caricare il file .glb. Potrebbe essere corrotto.");
            Destroy(currentModelContainer);
        }
    }
    #endregion
}