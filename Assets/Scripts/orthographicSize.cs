using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraAspectRatioFixer : MonoBehaviour
{
    // 基準とする解像度（例: 1920 x 1080）
    public float targetWidth = 1920f;
    public float targetHeight = 1080f;
    public float targetOrthographicSize = 5f;

    void Update()
    {
        float targetAspect = targetWidth / targetHeight;
        float currentAspect = (float)Screen.width / (float)Screen.height;

        Camera cam = GetComponent<Camera>();

        if (currentAspect >= targetAspect)
        {
            // 基準より横長の場合：高さを基準にする
            cam.orthographicSize = targetOrthographicSize;
        }
        else
        {
            // 基準より縦長の場合：幅を基準にしてサイズを拡大・縮小する
            cam.orthographicSize = targetOrthographicSize * (targetAspect / currentAspect);
        }
    }
}