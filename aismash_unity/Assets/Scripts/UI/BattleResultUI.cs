using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    public class BattleResultUI : MonoBehaviour
    {
        [Header("References")]
        public GameObject panel;
        public TextMeshProUGUI resultText;
        public Button restartButton;

        void Start()
        {
            if (panel != null) panel.SetActive(false);

            if (BattleManager.Instance != null)
                BattleManager.Instance.OnBattleEnd += ShowResult;

            if (restartButton != null)
                restartButton.onClick.AddListener(() =>
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name));
        }

        void ShowResult(int winnerIndex)
        {
            if (panel != null) panel.SetActive(true);
            if (resultText != null)
                resultText.text = winnerIndex switch
                {
                    0  => "1P WIN!",
                    1  => "2P WIN!",
                    _  => "DRAW"
                };
        }
    }
}
