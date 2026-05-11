using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
            {
                BattleManager.Instance.OnBattleEnd      += ShowResult;
                BattleManager.Instance.OnReturnedToSetup += HidePanel;
            }

            if (restartButton != null)
                restartButton.onClick.AddListener(OnRestart);
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

        void OnRestart()
        {
            HidePanel();
            BattleManager.Instance?.ReturnToSetup();
        }

        void HidePanel()
        {
            if (panel != null) panel.SetActive(false);
        }
    }
}
