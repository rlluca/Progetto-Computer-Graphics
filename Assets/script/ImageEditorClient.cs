using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ImageEditorClient : MonoBehaviour
{
    [Header("UI Elements - Edit")]
    public TMP_InputField editPromptInput; 
    public Button editModeButton;      
    public Button confirmEditButton;   
    public RawImage resultImage; 

    [Header("Riferimenti ad altri Script")]
    public VRImageEditor vrImageEditor;
    public ImageGeneratorClient mainGenerator; 

    [Header("Impostazioni Server")]
    public string serverUrlEdit = "http://192.168.80.138:8081/edit-image";
    public string sessionId = "edit-test"; 

    #region Classi Dati JSON
    [System.Serializable]
    private class EditRequest
    {
        public string session_id;
        public string image_path;
        public string mask_path; 
        public string mask_base64; // <-- Aggiunto questo campo essenziale per il Base64
        public string prompt;
    }

    [System.Serializable]
    private class EditResponse
    {
        public string edited_image_url;  
        public string edited_image_path; 
        public string status;
    }
    #endregion

    void Start()
    {
        if (editModeButton != null) editModeButton.onClick.AddListener(OnEditModeClicked);
        if (confirmEditButton != null) confirmEditButton.onClick.AddListener(OnConfirmEditClicked);
    }

    void OnEditModeClicked()
    {
        if (vrImageEditor != null)
        {
            vrImageEditor.EnableEditMode();
        }
    }

    void OnConfirmEditClicked()
    {
        string editPrompt = editPromptInput.text.Trim();
        
        if (string.IsNullOrEmpty(editPrompt))
        {
            Debug.LogWarning("Inserisci un prompt per l'edit prima di confermare!");
            return;
        }

        if (mainGenerator == null || string.IsNullOrEmpty(mainGenerator.lastImagePath))
        {
            Debug.LogWarning("Nessuna immagine di partenza trovata. Generane prima una!");
            return;
        }

        // Recupera i byte della maschera appena disegnata!
        string base64Mask = "";
        if (vrImageEditor != null)
        {
            byte[] maskBytes = vrImageEditor.GetMaskPNGBytes();
            if (maskBytes != null && maskBytes.Length > 0)
            {
                base64Mask = System.Convert.ToBase64String(maskBytes);
            }
        }

        if (string.IsNullOrEmpty(base64Mask))
        {
            Debug.LogWarning("Nessuna maschera trovata! Assicurati di aver disegnato sull'immagine.");
            return; // Blocchiamo la richiesta se non c'è una maschera valida
        }

        confirmEditButton.interactable = false;
        string originalImagePath = mainGenerator.lastImagePath;

        // Passiamo il base64Mask invece del percorso hardcodato
        StartCoroutine(SendEditImageRequest(originalImagePath, base64Mask, editPrompt));
    }

    IEnumerator SendEditImageRequest(string originalImagePath, string maskBase64, string prompt)
    {
        EditRequest body = new EditRequest
        {
            session_id = sessionId,
            image_path = originalImagePath,
            mask_path = "", // Lo lasciamo vuoto, il server Python userà il Base64
            mask_base64 = maskBase64, // <-- Passiamo l'immagine direttamente in JSON!
            prompt = prompt
        };

        string json = JsonUtility.ToJson(body);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(serverUrlEdit, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("Richiesta di Edit inviata al server (con maschera Base64). In attesa...");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Errore POST Edit: " + request.error + " | Dettagli: " + request.downloadHandler.text);
                confirmEditButton.interactable = true;
                yield break;
            }

            EditResponse response = JsonUtility.FromJson<EditResponse>(request.downloadHandler.text);

            if (response == null || response.status != "ok" || string.IsNullOrEmpty(response.edited_image_url))
            {
                Debug.LogError("Risposta Edit non valida dal server: " + request.downloadHandler.text);
                confirmEditButton.interactable = true;
                yield break;
            }

            Debug.Log("Edit completato! Scarico la nuova immagine da: " + response.edited_image_url);

            // 1. Mostriamo l'immagine a schermo
            yield return StartCoroutine(DownloadAndShowImage(response.edited_image_url));
            
            // 2. Aggiorniamo il path nello script principale
            mainGenerator.lastImagePath = response.edited_image_path; 
        }

        confirmEditButton.interactable = true;
    }

    IEnumerator DownloadAndShowImage(string imageUrl)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                
                // Assicurati che resultImage sia stato assegnato nell'Inspector!
                if (resultImage != null)
                {
                    resultImage.texture = texture;
                }
                else 
                {
                    Debug.LogError("resultImage non è stato assegnato nell'Inspector!");
                }
                
                if (vrImageEditor != null) vrImageEditor.isEditModeActive = false;
            }
            else
            {
                Debug.LogError("Errore download immagine modificata: " + request.error);
            }
        }
    }
}