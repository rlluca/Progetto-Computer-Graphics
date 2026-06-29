using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class VRImageEditor : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Impostazioni Smart Brush")]
    public int brushRadius = 10;
    public Color strokeColor = new Color(0f, 0.5f, 1f, 0.8f); 
    public Color fillColor = new Color(1f, 0f, 0f, 0.6f);     

    [Header("Stato Modifica")]
    public bool isEditModeActive = false;

    private RawImage targetImage;
    private Texture2D displayTexture; 
    private Texture2D pureMaskTexture; 
    private RectTransform rectTransform;

    private Color[] originalPixels;
    private List<Vector2Int> currentShapePolygon = new List<Vector2Int>();
    private Vector2Int lastDrawnPixel;

    void Start()
    {
        targetImage = GetComponent<RawImage>();
        rectTransform = GetComponent<RectTransform>();
    }

    public void EnableEditMode()
    {
        isEditModeActive = true;
        
        // IL FIX È QUI: Se la texture attuale della RawImage è diversa dalla nostra displayTexture,
        // significa che c'è una nuova immagine (es. appena scaricata dal server). Dobbiamo ricaricare!
        if (targetImage.texture != null && targetImage.texture != displayTexture)
        {
            InitializeTextures();
        }
        Debug.Log("Modalità Edit Attivata: Disegna a mano libera attorno all'oggetto.");
    }

    void InitializeTextures()
    {
        Texture2D sourceTex = targetImage.texture as Texture2D;
        if (sourceTex == null) return;

        int width = sourceTex.width;
        int height = sourceTex.height;

        // TRUCCO ANTIPROIETTILE: Copiamo la texture tramite la GPU (RenderTexture).
        // Questo ci permette di leggere i pixel SEMPRE, anche se l'immagine scaricata 
        // dal server non ha la spunta "Read/Write" abilitata!
        RenderTexture tmp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(sourceTex, tmp);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmp;

        Texture2D readableTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        readableTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readableTex.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmp);

        // Ora che abbiamo i pixel sicuri al 100%, salviamo lo stato iniziale
        originalPixels = readableTex.GetPixels();

        displayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        displayTexture.SetPixels(originalPixels);
        displayTexture.Apply();
        
        targetImage.texture = displayTexture; // Colleghiamo la tela alla UI

        pureMaskTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] blackPixels = new Color[width * height];
        for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
        pureMaskTexture.SetPixels(blackPixels);
        pureMaskTexture.Apply();

        // Puliamo la memoria
        Destroy(readableTex);
    }

    public void ResetMask()
    {
        if (displayTexture == null || pureMaskTexture == null || originalPixels == null) return;

        // Ci assicuriamo che lo schermo stia guardando la tela giusta
        if (targetImage.texture != displayTexture) targetImage.texture = displayTexture;

        // 1. Ripristina i pixel visibili
        displayTexture.SetPixels(originalPixels);
        displayTexture.Apply();

        // 2. Ripristina la maschera nascosta (tutto nero)
        Color[] blackPixels = new Color[pureMaskTexture.width * pureMaskTexture.height];
        for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
        
        pureMaskTexture.SetPixels(blackPixels);
        pureMaskTexture.Apply();

        currentShapePolygon.Clear();
        Debug.Log("Maschera resettata con successo.");
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isEditModeActive || displayTexture == null) return;

        currentShapePolygon.Clear();

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            Vector2Int pixelCoords = LocalPointToPixelCoords(localPoint);
            currentShapePolygon.Add(pixelCoords);
            lastDrawnPixel = pixelCoords;
            
            DrawCircle(pixelCoords.x, pixelCoords.y, strokeColor);
            displayTexture.Apply();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isEditModeActive || displayTexture == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            Vector2Int pixelCoords = LocalPointToPixelCoords(localPoint);
            
            if (Vector2Int.Distance(lastDrawnPixel, pixelCoords) > 2f)
            {
                currentShapePolygon.Add(pixelCoords);
                DrawContinuousLine(lastDrawnPixel, pixelCoords);
                lastDrawnPixel = pixelCoords;
                displayTexture.Apply(); 
            }
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isEditModeActive || displayTexture == null) return;

        if (currentShapePolygon.Count > 2) 
        {
            DrawContinuousLine(lastDrawnPixel, currentShapePolygon[0]);
            AutoFillDrawnShape();
        }

        displayTexture.Apply();
        pureMaskTexture.Apply();
    }

    private void AutoFillDrawnShape()
    {
        int width = displayTexture.width;
        int height = displayTexture.height;

        int minX = width, maxX = 0, minY = height, maxY = 0;
        foreach (Vector2Int pt in currentShapePolygon)
        {
            if (pt.x < minX) minX = pt.x;
            if (pt.x > maxX) maxX = pt.x;
            if (pt.y < minY) minY = pt.y;
            if (pt.y > maxY) maxY = pt.y;
        }

        minX = Mathf.Clamp(minX - 1, 0, width);
        maxX = Mathf.Clamp(maxX + 1, 0, width);
        minY = Mathf.Clamp(minY - 1, 0, height);
        maxY = Mathf.Clamp(maxY + 1, 0, height);

        Color32[] maskPixels = pureMaskTexture.GetPixels32();
        Color32[] displayPixels = displayTexture.GetPixels32();

        Color32 targetFillColor = fillColor;
        Color32 whiteMask = new Color32(255, 255, 255, 255);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (IsPointInPolygon(new Vector2Int(x, y), currentShapePolygon))
                {
                    int index = y * width + x;
                    displayPixels[index] = targetFillColor;
                    maskPixels[index] = whiteMask;
                }
            }
        }

        pureMaskTexture.SetPixels32(maskPixels);
        displayTexture.SetPixels32(displayPixels);
    }

    private bool IsPointInPolygon(Vector2Int pt, List<Vector2Int> polygon)
    {
        bool isInside = false;
        int count = polygon.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            if (((polygon[i].y > pt.y) != (polygon[j].y > pt.y)) &&
                (pt.x < (polygon[j].x - polygon[i].x) * (pt.y - polygon[i].y) / (float)(polygon[j].y - polygon[i].y) + polygon[i].x))
            {
                isInside = !isInside;
            }
        }
        return isInside;
    }

    private void DrawContinuousLine(Vector2Int start, Vector2Int end)
    {
        float dist = Vector2.Distance(start, end);
        int steps = Mathf.CeilToInt(dist / (brushRadius * 0.5f)); 

        for (int i = 0; i <= steps; i++)
        {
            Vector2 current = Vector2.Lerp(start, end, (float)i / steps);
            DrawCircle(Mathf.RoundToInt(current.x), Mathf.RoundToInt(current.y), strokeColor);
        }
    }

    private void DrawCircle(int centerX, int centerY, Color colorToApply)
    {
        for (int x = -brushRadius; x <= brushRadius; x++)
        {
            for (int y = -brushRadius; y <= brushRadius; y++)
            {
                if (x * x + y * y <= brushRadius * brushRadius)
                {
                    int targetX = centerX + x;
                    int targetY = centerY + y;

                    if (targetX >= 0 && targetX < displayTexture.width && targetY >= 0 && targetY < displayTexture.height)
                    {
                        displayTexture.SetPixel(targetX, targetY, colorToApply);
                        pureMaskTexture.SetPixel(targetX, targetY, Color.white);
                    }
                }
            }
        }
    }

    private Vector2Int LocalPointToPixelCoords(Vector2 localPoint)
    {
        float normalizedX = (localPoint.x - rectTransform.rect.x) / rectTransform.rect.width;
        float normalizedY = (localPoint.y - rectTransform.rect.y) / rectTransform.rect.height;

        int pixelX = Mathf.RoundToInt(normalizedX * displayTexture.width);
        int pixelY = Mathf.RoundToInt(normalizedY * displayTexture.height);
        
        return new Vector2Int(pixelX, pixelY);
    }

    public byte[] GetMaskPNGBytes()
    {
        if (pureMaskTexture == null) return null;
        pureMaskTexture.Apply(); 
        return pureMaskTexture.EncodeToPNG(); 
    }
}