# Epic 1: Features 獨立規格說明書

## 📄 Feature 1: 多租戶身分與安全邊界治理

* **功能群組：** 入口閘道與路由治理層（Gateway Layer）
* **實作元件：** `CloudKB.Gateway` (Yarp Reverse Proxy)

### 1. 功能概述

作為分布式微服務系統的單一入口，本功能負責對所有進入系統的 HTTP 與 WebSocket 連線進行身份驗證與多租戶環境隔離。系統必須確保所有進入微服務叢集的請求都具備明確且合法的租戶身分，並將該身分向下游轉遞，作為後續所有檔案儲存、快取與資料庫操作的最高層級隔離鍵。

### 2. 核心技術規格

* **認證機制：** 採用 JWT (JSON Web Token) 進行身分驗證。
* **租戶內容注入（Context Injection）：** 閘道端成功解密並驗證 JWT 後，提取其中的 `user_id`，並將其作為 `X-User-Id` 注入到轉發給下游微服務（如 Indexing API、Chat API）的 HTTP Header 中。
* **高並發傳輸優化：** 閘道端原生開啟 **HTTP/2 與 HTTP/3 協議**，利用其多工複用（Multiplexing）技術，允許前端多個分頁的長短期 SSE 串流共用單一 TCP 連線，免除瀏覽器因連線數耗盡而卡死的風險。
