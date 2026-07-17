using UnityEngine;
using UnityEngine.InputSystem;

public class Attack : MonoBehaviour
{
    //変数宣言
    float AttackTimer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
       
    }

    // Update is called once per frame
    void Update()
    {
        //パンチ
        if ((Keyboard.current.enterKey.wasPressedThisFrame || Gamepad.current.bButton.wasPressedThisFrame))
        {
            this.enabled = !this.enabled;
        }
    }
}
