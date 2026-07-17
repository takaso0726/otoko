using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class TitileText : MonoBehaviour
{
    //変数宣言
    float alpha;        //アルファ値用の変数
    private int clickCount = 0;　//クリック回数をカウント
    private Title titleScript; // タイトルのスクリプト

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        alpha = 1.0f;
        // シーン内からTitleスクリプトを紐付け
        titleScript = UnityEngine.Object.FindAnyObjectByType<Title>();
    }

    // Update is called once per frame
    void Update()
    {

        alpha = Mathf.Sin(Time.time);
        gameObject.GetComponent<TextMeshProUGUI>().color = new Color(1.0f, 1.0f, 1.0f, alpha);
    }

    // クリック回数を確認
    public void OnCursorClick()
    {
        clickCount++;
        if (clickCount >= 3)
        {
            if (titleScript != null)
            {
                titleScript.OnTextThriceClicked();
            }
            clickCount = 0;
        }
    }
}
