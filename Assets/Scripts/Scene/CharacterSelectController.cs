using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// キャラクターセレクト画面の制御。
/// 1P・2Pがそれぞれ独立したカーソルでキャラクターを選び、決定ボタンで確定する。
/// 両者の選択が確定したら、演出→ディレイを挟んでインゲームシーンへ遷移する。
///
/// ・カーソル移動のコルーチンとSE再生パターンは MainMenuController.cs を踏襲。
/// ・「条件成立→演出SE→WaitForSeconds→SceneManager.LoadScene」の流れは
///   旧SelectController(SelectController2.cs)のPlayCrackAndTransition()を踏襲。
/// </summary>
public class CharacterSelectController : MonoBehaviour
{
    [System.Serializable]
    public class CharacterEntry
    {
        public string characterName;  // 表示名
        public RectTransform anchor;  // グリッド上の位置（各キャラアイコンのUI要素）
        public Sprite portrait;       // カーソルを合わせた時に表示するドアップ画像
    }

    [System.Serializable]
    public class PlayerSelector
    {
        public string playerName;      // "1P" / "2P" など表示用
        public RectTransform cursor;   // このプレイヤー用カーソル
        public GameObject readyMark;   // 決定後に表示する「READY」表示（Inspectorで非アクティブにしておく）
        public Image portraitImage;    // 選択中キャラのドアップ表示用（1Pは画面左、2Pは画面右に配置しておく）

        [HideInInspector] public int currentIndex;   // allowedIndices内でのローカルなカーソル位置
        [HideInInspector] public bool decided;
        [HideInInspector] public int[] allowedIndices; // このプレイヤーが選択できるcharactersのインデックス一覧（画面左右で自動振り分け）
    }

    [Header("キャラクター一覧（グリッド順）")]
    [SerializeField] CharacterEntry[] characters;

    [Header("プレイヤー")]
    [SerializeField] PlayerSelector player1;
    [SerializeField] PlayerSelector player2;
    [SerializeField] float cursorMoveSpeed = 12f;

    [Header("SE")]
    [SerializeField] AudioSource se;
    [SerializeField] AudioClip moveSE;      // カーソル移動時
    [SerializeField] AudioClip decideSE;    // 1人が決定した時
    [SerializeField] AudioClip bothReadySE; // 両者決定＆遷移演出時

    [Header("遷移設定")]
    [SerializeField] string nextSceneName = "InGame1v1";
    [SerializeField] float transitionDelay = 0.7f;

    bool isTransitioning;
    Coroutine p1CursorRoutine;
    Coroutine p2CursorRoutine;

    void Start()
    {
        if (characters.Length == 0) return;

        BuildSideGroups();

        if (player1.allowedIndices.Length == 0)
        {
            Debug.LogWarning("[CharacterSelectController] 1P側（画面左）に該当するキャラクターがありません。anchorの配置を確認してください。");
        }
        if (player2.allowedIndices.Length == 0)
        {
            Debug.LogWarning("[CharacterSelectController] 2P側（画面右）に該当するキャラクターがありません。anchorの配置を確認してください。");
        }

        player1.currentIndex = 0;
        player2.currentIndex = 0;

        if (player1.allowedIndices.Length > 0)
        {
            player1.cursor.position = characters[player1.allowedIndices[0]].anchor.position;
        }
        if (player2.allowedIndices.Length > 0)
        {
            player2.cursor.position = characters[player2.allowedIndices[0]].anchor.position;
        }

        SetReadyMarkActive(player1, false);
        SetReadyMarkActive(player2, false);
        UpdatePortrait(player1);
        UpdatePortrait(player2);
    }

    // characters配列を、各anchorの画面X座標が画面中央より左か右かで
    // 1P側（左半分）／2P側（右半分）に自動振り分けする。
    // ※ Canvasが Screen Space - Overlay の場合、anchor.position は画面ピクセル座標と一致するため
    //   Screen.width/2 との比較で左右判定ができる。
    //   Screen Space - Camera / World Space Canvas を使っている場合は判定方法の見直しが必要。
    void BuildSideGroups()
    {
        var leftList = new List<int>();
        var rightList = new List<int>();
        float centerX = Screen.width / 2f;

        for (int i = 0; i < characters.Length; i++)
        {
            var anchor = characters[i].anchor;
            if (anchor == null) continue;

            if (anchor.position.x < centerX)
            {
                leftList.Add(i);
            }
            else
            {
                rightList.Add(i);
            }
        }

        player1.allowedIndices = leftList.ToArray();
        player2.allowedIndices = rightList.ToArray();
    }

    void Update()
    {
        if (isTransitioning) return;

        HandlePlayer(player1, ReadP1Direction(), ReadP1Decide(), ReadP1Cancel(), ref p1CursorRoutine);
        HandlePlayer(player2, ReadP2Direction(), ReadP2Decide(), ReadP2Cancel(), ref p2CursorRoutine);

        // 両者が決定していたら遷移演出へ
        if (player1.decided && player2.decided)
        {
            StartCoroutine(BothReadyAndTransition());
        }
    }

    void HandlePlayer(PlayerSelector p, Vector2Int inputDir, bool decide, bool cancel, ref Coroutine routine)
    {
        if (p.decided)
        {
            // 決定後はキャンセル入力のみ受け付けて選び直しできるようにする
            if (cancel)
            {
                p.decided = false;
                SetReadyMarkActive(p, false);
            }
            return;
        }

        // 担当側（左 or 右）にキャラが1体も無い場合は操作させない
        if (p.allowedIndices == null || p.allowedIndices.Length == 0) return;

        // 同一フレームで斜め入力が来た場合は左右を優先する（上下と同時に判定すると挙動が分かりづらくなるため）
        Vector2Int navDir = inputDir.x != 0 ? new Vector2Int(inputDir.x, 0) : new Vector2Int(0, inputDir.y);

        if (navDir != Vector2Int.zero)
        {
            int currentGlobalIndex = p.allowedIndices[p.currentIndex];
            int nextGlobalIndex = FindNearestInDirection(p, currentGlobalIndex, new Vector2(navDir.x, navDir.y));

            if (nextGlobalIndex >= 0)
            {
                int localIndex = System.Array.IndexOf(p.allowedIndices, nextGlobalIndex);
                if (localIndex >= 0)
                {
                    p.currentIndex = localIndex;
                    PlaySE(moveSE);

                    if (routine != null) StopCoroutine(routine);
                    routine = StartCoroutine(MoveCursorSmooth(p.cursor, characters[nextGlobalIndex].anchor.position));

                    UpdatePortrait(p);
                }
            }
        }

        if (decide)
        {
            p.decided = true;
            SetReadyMarkActive(p, true);
            PlaySE(decideSE);
        }
    }

    // 現在位置(fromGlobalIndex)から見て、dir方向にある「担当側キャラの中で一番近いもの」のグローバルインデックスを返す。
    // 見つからなければ-1。
    // dirが伸びる方向（主軸）の距離を優先しつつ、主軸から外れる（横ズレ・縦ズレ）ほどペナルティを与えることで、
    // 単純なグリッドでなくても自然に「右にある一番近いキャラ」「上にある一番近いキャラ」を選べるようにしている。
    int FindNearestInDirection(PlayerSelector p, int fromGlobalIndex, Vector2 dir)
    {
        var fromAnchor = characters[fromGlobalIndex].anchor;
        if (fromAnchor == null) return -1;

        Vector2 currentPos = fromAnchor.position;
        Vector2 dirNormalized = dir.normalized;
        Vector2 perpAxis = new Vector2(-dirNormalized.y, dirNormalized.x); // dirに直交する軸

        int bestIndex = -1;
        float bestScore = float.MaxValue;

        foreach (int idx in p.allowedIndices)
        {
            if (idx == fromGlobalIndex) continue;

            var anchor = characters[idx].anchor;
            if (anchor == null) continue;

            Vector2 offset = (Vector2)anchor.position - currentPos;

            float primary = Vector2.Dot(offset, dirNormalized);
            if (primary <= 0.01f) continue; // 指定方向とは逆・同位置にあるものは除外

            float perpendicular = Mathf.Abs(Vector2.Dot(offset, perpAxis));

            // 主軸方向の距離を基本スコアにしつつ、軸ズレには重めのペナルティを掛けて
            // 「まっすぐ近い」候補を優先する
            float score = primary + perpendicular * 2f;

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = idx;
            }
        }

        return bestIndex;
    }

    // GameObjectの「疑似null（参照は残っているが実体が破棄されている状態）」でも
    // 安全に判定できるよう、?.ではなく明示的なUnityの==比較でチェックする
    void SetReadyMarkActive(PlayerSelector p, bool active)
    {
        if (p.readyMark != null)
        {
            p.readyMark.SetActive(active);
        }
    }

    // カーソルが乗っているキャラクターのドアップ画像を、そのプレイヤー専用のImageに反映する
    // （1P用Imageは画面左、2P用Imageは画面右のRectTransformに配置しておく想定）
    void UpdatePortrait(PlayerSelector p)
    {
        if (p.portraitImage == null) return;
        if (p.allowedIndices == null || p.allowedIndices.Length == 0) return;

        int charIndex = p.allowedIndices[p.currentIndex];
        var sprite = characters[charIndex].portrait;
        p.portraitImage.sprite = sprite;

        // portrait未設定のキャラの場合はImageを非表示にして「空の白い四角」が出ないようにする
        p.portraitImage.enabled = sprite != null;
    }

    // AudioClipがInspectorで未設定の場合にPlayOneShot(null)警告が出るのを防ぐ
    void PlaySE(AudioClip clip)
    {
        if (se != null && clip != null)
        {
            se.PlayOneShot(clip);
        }
    }

    IEnumerator MoveCursorSmooth(RectTransform cursor, Vector3 target)
    {
        while (Vector3.Distance(cursor.position, target) > 0.5f)
        {
            cursor.position = Vector3.MoveTowards(cursor.position, target, cursorMoveSpeed * Time.deltaTime * 1000f);
            yield return null;
        }
        cursor.position = target;
    }

    IEnumerator BothReadyAndTransition()
    {
        isTransitioning = true;

        PlaySE(bothReadySE);

        yield return new WaitForSeconds(transitionDelay);

        SceneManager.LoadScene(nextSceneName);
    }

    // ---- 入力読み取り ----
    //
    // PCに接続された2台のコントローラーを同時に使えるように、
    // 1P = Gamepad.all[0]、2P = Gamepad.all[1] とインデックスを固定して割り当てる。
    // （以前の「2台無い場合は1台を共用」という仕様だと、2台繋いでいても
    //   1Pがキーボード固定のため2台目を活かせなかった）
    //
    // 1Pのみキーボードも合わせて受け付ける（コントローラー無しでも1人でテスト可能にするため）。
    // キーボード／ゲームパッドどちらの入力も同一フレームでは併用OK。
    //
    // 各Read〜Direction()はVector2Int(x, y)を返す。x: 右+1/左-1、y: 上+1/下-1。

    // 1P: ゲームパッド0番 + キーボード（矢印キー／WASD、決定：Enter or ボタンSouth、キャンセル：Esc or ボタンEast）
    Vector2Int ReadP1Direction()
    {
        var kb = ReadDirectionFromKeyboard();
        if (kb != Vector2Int.zero) return kb;

        return ReadDirectionFromGamepad(GetGamepad(0));
    }
    bool ReadP1Decide()
    {
        if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) return true;
        var pad = GetGamepad(0);
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }
    bool ReadP1Cancel()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
        var pad = GetGamepad(0);
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    // 2P: ゲームパッド1番のみ（1Pのゲームパッドとは独立して同時入力を受け付ける）
    Vector2Int ReadP2Direction()
    {
        return ReadDirectionFromGamepad(GetGamepad(1));
    }
    bool ReadP2Decide()
    {
        var pad = GetGamepad(1);
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }
    bool ReadP2Cancel()
    {
        var pad = GetGamepad(1);
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    // キーボードの矢印キー／WASDから方向を読み取る
    Vector2Int ReadDirectionFromKeyboard()
    {
        if (Keyboard.current == null) return Vector2Int.zero;

        int x = 0;
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame) x = 1;
        else if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame) x = -1;

        int y = 0;
        if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) y = 1;
        else if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) y = -1;

        return new Vector2Int(x, y);
    }

    // ゲームパッドのD-Pad／左スティックから方向を読み取る
    Vector2Int ReadDirectionFromGamepad(Gamepad pad)
    {
        if (pad == null) return Vector2Int.zero;

        int x = 0;
        if (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame) x = 1;
        else if (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame) x = -1;

        int y = 0;
        if (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame) y = 1;
        else if (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame) y = -1;

        return new Vector2Int(x, y);
    }

    // index番目に接続されているゲームパッドを返す（未接続ならnull）
    Gamepad GetGamepad(int index)
    {
        return index < Gamepad.all.Count ? Gamepad.all[index] : null;
    }
}
