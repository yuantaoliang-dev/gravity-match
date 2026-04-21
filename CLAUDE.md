# 專案規則

## 溝通
- 全程用繁體中文回覆
- 程式碼註解用英文

## 開發流程
- 直接在 main 分支修改，不要開新分支

## 專案資訊
- 這是 Gravity Match: Event Horizon 的 Unity 2D 移植版
- 原版 HTML5 遊戲在 gravity_match_v21.html，所有遊戲邏輯請對照這個檔案
- 目標平台：Android
- Unity 版本：6.3 LTS，使用 URP 2D
- Shader 用 "Universal Render Pipeline/2D/Sprite-Unlit-Default"，找不到時 fallback 到 "Sprites/Default"

## 程式碼規範
- 遊戲常數都在 GameConstants.cs，不要硬編碼數值
- 關卡資料在 LevelDataBuilder.cs
- 核心遊戲邏輯在 GameManager.cs
- 修改前先讀取相關的 .cs 檔案了解現有結構

## 測試
- 修改後確保 Unity Console 沒有紅色錯誤
- 重要邏輯加 Debug.Log 方便除錯