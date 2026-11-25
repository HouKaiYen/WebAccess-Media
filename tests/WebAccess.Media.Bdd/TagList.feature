# language: zh-TW
功能: 工程設定取得 WebAccess 測點清單
  場景大綱: 讀取已驗證的測點清單格式
    假設 WebAccess 以「<清單>」和「<說明>」回傳測點
    當 工程設定查詢測點清單
    那麼 可選取測點 Pump01 且說明為「消防泵」

    例子:
      | 清單    | 說明        |
      | Tags    | Description |
      | TagList | Desc        |

  場景: 回應總筆數與清單不符
    假設 WebAccess 回傳不完整的測點清單
    當 工程設定查詢測點清單
    那麼 不提供不完整的測點清單
