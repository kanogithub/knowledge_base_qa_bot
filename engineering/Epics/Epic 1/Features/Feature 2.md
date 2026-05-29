## 📄 Feature 2: 異步事件驅動文件寫入流

* **功能群組：** 寫入 API 接收層（Ingest API Layer）
* **實作元件：** `CloudKB.ApiService.Indexing` + `AWS S3 / MinIO`

### 1. 功能概述

為應付生產環境的高寫入 QPS，本功能負責實現「寫入流」的非阻塞（Non-blocking）、快進秒回機制。當前端使用者上傳 Markdown 檔案時，API 服務只專職執行輕量級的儲存與任務派發，隨即結束 HTTP 連線，將繁重的解析與統計任務完全移交給背景佇列。

### 2. 核心技術規格

* **串流檔案儲存：** 接收前端上傳的 Markdown 檔案，以 Stream 方式直接寫入分散式物件儲存（S3 或 MinIO），儲存路徑嚴格按租戶識別碼進行物理隔離：`/{user_id}/raw/{filename}.md`。
* **任務事件派發：** 成功寫入 S3 後，將任務資訊包裝成強型別的 `CompileKnowledgeTask` JSON 契約訊息，推送到 **RabbitMQ** 訊息佇列中。
* **非阻塞回應（Fire-and-Forget）：** 完成訊息推送後，API 服務不等待後台編譯結果，必須在 **100 毫秒內** 立即對前端回傳 `HTTP 202 Accepted`（Payload 內含系統生成的 `TaskId`），立刻釋放 Kestrel Web 伺服器的執行緒，防止並發上傳時引發的執行緒飢餓（Thread Starvation）。
