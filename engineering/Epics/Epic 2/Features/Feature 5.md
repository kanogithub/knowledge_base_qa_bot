## 📄 Feature 5: 多租戶問答閘道路由與高並發傳輸優化

* **功能群組：** 入口閘道與路由治理層 (Gateway Layer)
* **實作元件：** `CloudKB.Gateway` (Yarp Reverse Proxy)

### 1. 功能概述

本功能作為問答流的入口防線，負責對使用者的 Chat 請求進行 JWT 身分驗證與租戶 Context 注入。更重要的是，它必須在網頁伺服器（Kestrel）端優化 HTTP 傳輸協議，以確保高 QPS 流量下的多租戶短生命週期串流（Short-Lived SSE）能夠順暢不卡頓。

### 2. 核心技術規格

* **租戶身分防禦：** 攔截 `POST /api/chat` 請求，驗證 JWT Token。提取 `user_id` 並作為 `X-User-Id` 注入 HTTP 標頭，確保下游的 Chat 服務能絕對隔離地讀取該租戶的快取地圖與事實文本。
* **HTTP/2 & HTTP/3 串流優化：** 由於高 QPS 下會有大量用戶同時接收 AI 的逐字串流回應，閘道端必須強制啟用 HTTP/2 或 HTTP/3。利用其單一連線多工複用（Multiplexing）的特性，降低伺服器維持大量並發 HTTP 連線的 TCP 握手與記憶體開銷。
