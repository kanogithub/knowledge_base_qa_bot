## Epic 1: 事件驅動分布式多租戶知識編譯與通知流水線 (The Write Pipeline)

* **核心目標：** 解決使用者上傳多個 Markdown 檔案後，系統如何在背景安全、隔離、高效地將其「編譯」成顯式索引（Explicit Index），並在完工時透過純 HTTP 串流主動通知使用者。

### ── Feature 1: 多租戶身分與安全邊界治理 (多租戶隔離基礎)

* **功能描述：** 負責整個系統的入口防禦與租戶資料隔離。系統必須確保所有進入微服務叢集的請求都具備明確的租戶身分，並作為後續所有儲存與快取的唯一隔離鍵。
* **驗證與實作指標：**
* API Gateway (Yarp) 能正確攔截、驗證 JWT Token，並將解析出的 `user_id` 作為 `X-User-Id` Header 注入下游無狀態服務。
* 閘道端原生開啟 HTTP/2 與 HTTP/3 支援，確保前端多個分頁的 SSE 串流能多工複用（Multiplexing）於單一 TCP 連線中，免除瀏覽器連線數耗盡風險。



### ── Feature 2: 異步事件驅動文件寫入流 (快進秒回機制)

* **功能描述：** 實現「寫入流」的非阻塞、高吞吐量處理。前端使用者上傳檔案時，API 必須做到秒回，並將繁重的解析任務完全移交到背景佇列。
* **驗證與實作指標：**
* `Indexing API` 接收到 Markdown 檔案後，能以 Stream 方式快速寫入 S3/MinIO 租戶隔離路徑（`/{user_id}/raw/`）。
* 成功發送 `CompileKnowledgeTask` 結構化訊息至 RabbitMQ 任務佇列。
* API 必須在 100 毫秒內立即對前端回傳 `HTTP 202 Accepted`（附帶 TaskId），釋放執行緒，避免 Kestrel 連線卡死。



### ── Feature 3: 多租戶 Markdown 知識編譯與顯式索引維護 (背景運算引擎)

* **功能描述：** 本專案最核心的「運算引擎」。背景 Worker 異步消費佇列任務，執行 Karpathy 的 LLM Wiki Pattern（將 Markdown 結構化編譯並建立輕量地圖）。
* **驗證與實作指標：**
* `Index Worker` 背景服務成功利用 Redis 分散式鎖（`lock:index:{user_id}`）確保同租戶並發寫入時的互斥性，防止索引統計（DF, TF）被互相覆蓋。
* 能正確執行 Line-by-Line 解析，將 Markdown 依 Heading 切分成 `TenantSection`，並透過 EF Core 10 批量寫入（Bulk Operations）PostgreSQL 資料庫。
* 算好該租戶專屬的 BM25 統計指標（DF, TF, avgdl），生成不含內文的輕量化 `TenantKbIndex` JSON 並刷新至 Redis 快取。



### ── Feature 4: 基於 Redis Pub/Sub 的全域長駐型通知串流 (長生命通知 SSE)

* **功能描述：** 解決背景長任務的即時狀態回報 UX 痛點。當背景 Worker 在處理或完成 Index 時，前端必須能收到實時的進度跳動與完工通知。
* **驗證與實作指標：**
* 前端在使用者登入系統時，即成功建立一條持續開啟、低負載的全域事件監聽通道（`GET /api/notifications/stream`）。
* `Notification Service` 成功訂閱 Redis 的多租戶廣播頻道（`ch:notifications:{user_id}`）。
* 當背景 Worker 發佈事件時，該服務能透過 `text/event-stream` 將進度與完工狀態精準推送給該租戶的前端畫面上（如 `Processing` 或 `IndexCompleted`）。
