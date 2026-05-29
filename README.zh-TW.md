# Project Cloud-KB: 分散式多租戶 Markdown 知識庫系統

Project Cloud-KB 是一個雲端原生、分散式的 RAG (檢索增強生成) 知識庫問答助手。本專案基於 **.NET 10** 與 **.NET Aspire 10** 生態系建構，包含非同步知識編譯管線（整合 PostgreSQL、Redis、RabbitMQ 及 MinIO 物件儲存）以及一個現代化的 React 智能對話介面，並能動態顯示所引用文檔來源的 BM25 檢索分數。

---

## 準備工作 (從零開始安裝)

在運行此專案前，必須先在電腦中配置以下環境。

### 1. Docker Desktop
本專案使用 **.NET Aspire** 來自動編排所有相依的容器服務（Postgres, Redis, RabbitMQ, MinIO）。Aspire 必須依賴容器運行環境：
* **Windows**: 下載並安裝 [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/)。請確保啟動了 WSL 2 整合後端。
* **macOS**: 下載並安裝 [Docker Desktop for Mac](https://www.docker.com/products/docker-desktop/)。請依據你的 Mac 硬體選擇 Apple Silicon (M1/M2/M3/M4) 或 Intel 晶片版本。
* *提示：請確認 Docker Desktop 已啟動並在背景運行。*

### 2. .NET 10 SDK 與 Aspire 工作負載
* 下載並安裝 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
* 安裝完成後，開啟終端機（Windows 的 PowerShell/CMD 或 macOS 的 Terminal）執行以下指令來安裝 Aspire 工具集：
  ```bash
  dotnet workload install aspire
  ```

### 3. Node.js (v18+) 與 npm
前端專案使用 React 搭配 Vite 開發。
* **Windows**: 使用 [Node.js 官方安裝檔](https://nodejs.org/) 或使用 [nvm-windows](https://github.com/coreybutler/nvm-windows) 管理版本。
* **macOS**: 使用 [Node.js 官方安裝檔](https://nodejs.org/)、Homebrew (`brew install node`)，或使用 [nvm](https://github.com/nvm-sh/nvm) 安裝。

---

## 專案配置指南

在啟動專案之前，你需要配置 AI API 密鑰以及調整 BM25 檢索參數。

### 1. AI 代理 API Key 配置 (OpenAI 或 Gemini)
RAG 對話引擎預設支援 Gemini 與 OpenAI 生成回答。你可以在 [appsettings.json](./src/CloudKB.ApiService.Chat/appsettings.json) 的 `LlmProviders:Priority` 來決定模型的優先載入順序。

你可以選擇**以下兩種方式之一**來設置 API Key：

#### 方法 A：使用 .NET User Secrets 安全配置 (推薦)
使用 User Secrets 可以將金鑰存在本機的加密區，避免祕鑰被意外 commit 到 Git 上。請在終端機中，切換到專案目錄並執行：
* **配置 Gemini (預設)**:
  ```bash
  dotnet user-secrets set "LlmProviders:Gemini:ApiKey" "你的_GEMINI_API_KEY" --project src/CloudKB.ApiService.Chat
  ```
* **配置 OpenAI**:
  ```bash
  dotnet user-secrets set "LlmProviders:OpenAI:ApiKey" "你的_OPENAI_API_KEY" --project src/CloudKB.ApiService.Chat
  ```

#### 方法 B：直接修改 `appsettings.json`
直接開啟 `src/CloudKB.ApiService.Chat/appsettings.json` 並修改：
```json
  "LlmProviders": {
    "Priority": [ "Gemini", "OpenAI" ],
    "Gemini": {
      "ApiKey": "你的_GEMINI_API_KEY",
      "ModelName": "gemini-2.5-flash",
      "Endpoint": "https://generativelanguage.googleapis.com/v1beta/openai/"
    },
    "OpenAI": {
      "ApiKey": "你的_OPENAI_API_KEY",
      "ModelName": "gpt-4o-mini",
      "Endpoint": "https://api.openai.com/v1/"
    }
  }
```

---

### 2. BM25 檢索閾值與核心引數設定
你可以透過修改 `src/CloudKB.ApiService.Chat/appsettings.json` 中的 `"BM25"` 區塊來客製化搜尋引擎的精準度（亦可使用 User Secrets 覆寫）：

* **`RetrievalScoreThreshold`** (預設值：`0.5`):
  * **關鍵設定**：若使用者提問後，系統經 BM25 算出的最高文檔關聯分數「低於」此閥值，則後端會拒絕回答，並回傳：「*我無法從現有的知識庫中確認此訊息。*」。
  * *測試技巧：在開發測試階段，若希望不論相似度分數高低皆強制提供引用回答，可將此閥值設定為 `0.0`。*
* **`K1`** (預設值：`1.2`) 與 **`B`** (預設值：`0.75`): 用於調整詞頻飽和度以及文檔長度歸一化幅度的 BM25 標準參數。
* **`HeadingBoost`** (預設值：`1.5`): 若提問詞匹配到文檔的標題（Heading），會給予此加權乘數，提升標題被引用的權重。
* **`TopK`** (預設值：`3`): 檢索出關聯度最高的前幾筆文檔段落送進 LLM 上下文（Context）中生成答覆。

---

## 快速開始 (開發模式)

### 步驟 1: 安裝前端套件並進行編譯
啟動後端服務前，必須先還原前端 React 的套件並進行編譯打包，如此一來 Gateway API 伺服器才能正確載入網頁。

1. 打開終端機，切換到前端 React 目錄：
   ```bash
   cd src/CloudKB.Web
   ```
2. 安裝 npm 相依套件：
   ```bash
   npm install
   ```
3. 執行生產環境編譯（產物將自動輸出到 Gateway API 專案的 `wwwroot/` 靜態檔案夾中）：
   ```bash
   npm run build
   ```

### 步驟 2: 啟動專案 (.NET Aspire 自動編排)
你**不需要**手動在 Docker 跑 `docker run` 去架設 Postgres、Redis、RabbitMQ 或 MinIO。 .NET Aspire 會替你拉取映像檔、建立容器、動態注入連線字串，並同時拉起所有後端微服務。

1. 終端機回到專案根目錄下。
2. 執行 Aspire AppHost 啟動專案：
   * **Windows / macOS**:
     ```bash
     dotnet run --project src/CloudKB.AppHost
     ```
3. 成功啟動後，主控台（console）的輸出會提供一個 **Aspire Dashboard** 的控制面板網址（例如：`http://localhost:17222/` 或其他隨機連接埠）。

### 步驟 3: 進入系統
1. 在瀏覽器打開剛才的 Aspire Dashboard 連結。
2. 控制面板上會列出目前運作中的所有服務：
   * **apiservice-chat**: RAG 問答生成引擎。
   * **apiservice-indexing**: 文件處理與 Redis BM25 索引計算。
   * **apiservice-notification**: SSE 即時通知推播。
   * **gateway**: API 網關（代理所有路由，並託管 React 前端靜態資源）。
   * **worker-indexer**: 背景隊列消費者。
3. 點選 **gateway** 旁邊的 Endpoint 連結（例如 `http://localhost:5000` 或隨機分配的 https 埠）。
4. 網頁開啟後即會顯示 **Cloud-KB Portal** 登入介面。

---

## 如何測試與使用

### 1. 註冊帳號與登入
* 你可以使用系統內置的測試租戶帳號：
  * **帳號 / Tenant ID**: `tenant-01`
  * **密碼**: `password`
* 或者點擊 **Create Tenant Account** 註冊一個全新租戶帳號並登入。

### 2. 上傳文件並建立索引
* 拖曳或手動選擇 `.md` 格式的 Markdown 檔案。
* 藉由即時的 SSE 連線，你將看到文件狀態從 **Queued** $\rightarrow$ **Indexing** $\rightarrow$ **Indexed** 即時轉換。
* 點選 **垃圾桶圖示** 可以直接刪除已索引的文檔，系統會從 Postgres 刪除段落、從 MinIO 儲存清除檔案，並動態觸發 Redis 中 BM25 檢索索引的重構。

### 3. RAG 知識庫問答
* 針對已上傳文件的內容進行提問。
* AI 助手會以打字機流式輸出（SSE Stream）回答。
* **引用標註與 BM25 分數**：
  * 當回答涉及具體文檔時，會以學術標號如 `[1]`、`[2]` 作為行內引用。
  * 訊息下方會顯示 **Sources** 列表按鈕，並於每個文件標題旁渲染出該段落的 **BM25 檢索匹配分數**（例如：`(Score: 3.52)`）。
  * 點選行內引用 `[2]` 或下方的 Source 按鈕，即可從底部滑出詳細資訊 Drawer，精準查看 `BM25 Retrieval Score` 到小數點後四位。
