# language: zh-TW
功能: 從 WebAccess 更新告警
  場景: 清單缺漏時保留告警，完整空清單才解除
    假設 WebAccess 已回報測點 A 的告警
    當 WebAccess 回傳不完整清單
    那麼 保留測點 A 並顯示連線異常
    當 WebAccess 回傳完整空清單
    那麼 恢復待機並顯示已連線
