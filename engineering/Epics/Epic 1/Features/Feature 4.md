## 📄 Feature 4: 基於 Redis Pub/Sub 的全域長駐型通知串流

* **功能群組：** 即時通訊與狀態回報層（Real-time Notification Layer）
* **實作元件：** `CloudKB.ApiService.Notification` + `Redis Pub/Sub`

### 1. 功能概述

負責解決背景長任務（Index）處理時的即時狀態回報，提供優良的使用者體驗（UX）。當背景 Worker 在處理 Markdown 或完成編譯時，系統必須能跨服務、跨實例將實時進度推送到使用者的前端畫面上。

### 2. 核心技術規格

* **全域長駐通知管道（Long-Lived SSE）：** 使用者登入系統時，前端即使用原生 `EventSource` API 與此服務建立一條持續開啟、低負載的 HTTP SSE 管道（`GET /api/notifications/stream`）。管道在無任務時保持靜止，僅定時傳送微量的 `keep-alive` 心跳包。
* **跨服務事件訂閱（Pub/Sub）：** 本服務啟動時，會透過 `StackExchange.Redis` 驅動程式訂閱 Redis 的多租戶廣播頻道 `ch:notifications:{user_id}`。
* **即時事件轉發：** 當背景 `Index Worker` 處理到不同階段並向 Redis 發佈事件（如 `Processing` 或 `IndexCompleted`）時，本服務會第一時間捕捉到該事件，並立即將其轉化為標準的 `text/event-stream` 格式，透過長駐管道精準推送到 Tammy 的前端畫面上，實現免重整的即時 UI 狀態跳動。