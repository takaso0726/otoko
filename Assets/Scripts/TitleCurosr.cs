using UnityEngine;
using UnityEngine.InputSystem;

public class TitleCurosr : MonoBehaviour
{
    // スティックでの移動速度
    [SerializeField] private float speed = 500.0f;

    // 移動範囲の制限（画面外に行かないように調整するための値）
    [SerializeField] private Vector2 moveLimit = new Vector2(960.0f, 540.0f);

    // ターゲットとなるテキストのRectTransform
    [SerializeField] private RectTransform targetTextRect;
    [SerializeField] private float hitDistance = 100f; // 当たり判定の広さ

    // タイトルが重なった時
    private TitileText hoveredText;
    private RectTransform rectTransform;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Canvas内での位置制御のトランスフォームを取得
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    // Update is called once per frame
    void Update()
    {
        Vector2 stickInput = Vector2.zero;
        if (Gamepad.current != null)
        {
            //右スティック（rightStick）の入力を取得 (-1.0 ～ 1.0)
            stickInput = Gamepad.current.rightStick.ReadValue();

            //文字が重なった時だけ反応
            if (Gamepad.current.aButton.wasPressedThisFrame && hoveredText != null)
            {
                Debug.Log("Aボタンが押されました");
                hoveredText.OnCursorClick();
            }
        }

        //スティックの移動処理
        if (stickInput.magnitude > 0.1f && rectTransform != null)
        {
            Vector2 currentPos = rectTransform.anchoredPosition;
            currentPos.x += stickInput.x * speed * Time.deltaTime;
            currentPos.y += stickInput.y * speed * Time.deltaTime;

            currentPos.x = Mathf.Clamp(currentPos.x, -moveLimit.x, moveLimit.x);
            currentPos.y = Mathf.Clamp(currentPos.y, -moveLimit.y, moveLimit.y);

            rectTransform.anchoredPosition = currentPos;
        }

        //当たり判定チェック
        if (targetTextRect != null && rectTransform != null)
        {
            float distance = Vector2.Distance(rectTransform.anchoredPosition, targetTextRect.anchoredPosition);

            if (distance < hitDistance)
            {
                if (hoveredText == null)
                {
                    hoveredText = targetTextRect.GetComponent<TitileText>();
                    if (hoveredText != null) Debug.Log("重なりました");
                }
            }
            else
            {
                if (hoveredText != null)
                {
                    Debug.Log("離れました");
                    hoveredText = null;
                }
            }
        }
    }
}
