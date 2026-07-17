using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class GameOver : MonoBehaviour
{
    Canvas endImage1;
    Canvas endImage2;
    Canvas endImage3;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        endImage1 = GameObject.Find("Canvas").gameObject.GetComponent<Canvas>();
        endImage2 = GameObject.Find("Canvas(1)").gameObject.GetComponent<Canvas>();
        endImage3 = GameObject.Find("Canvas(2)").gameObject.GetComponent<Canvas>();
    }

    // Update is called once per frame
    void Update()
    {
        //ƒL[“ü—Í‚Å‰æ‘œ•ÏX
        if (Keyboard.current.anyKey.wasPressedThisFrame)
        {
            
        }
    }
}
