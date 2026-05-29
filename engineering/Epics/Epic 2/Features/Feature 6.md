## 📄 Feature 6: 低延遲記憶體內路由與短生命週期問答串流

* **功能群組：** 讀取與問答核心運算層 (Read & Chat Core Layer)
* **實作元件：** `CloudKB.ApiService.Chat` + `Redis (Index Cache)` + `PostgreSQL` + `Microsoft.Extensions.AI`

### 1. 功能概述

本功能是系統面對使用者高高頻提問的「第一線戰術引擎」。它利用極致最佳化的記憶體內（In-Memory）運算進行顯式索引地圖導航，精準篩選事實文本，並透過短生命週期的 SSE 串流將答案逐字推回前端，在回答完畢後立即釋放資源。

### 2. 核心技術規格

* **快取地圖導航（In-Memory BM25）：** 收到提問後，以 $\mathcal{O}(1)$ 的速度從 Redis 讀取該租戶輕量化、不含重型內文的 `TenantKbIndex`。利用 .NET 10 的 `FrozenDictionary` 在 C# 記憶體內進行極速的 BM25 評分與 Heading Boost 標題加權計算。
* **弱檢索早期退出機制（Early Exit）：** **【Prod 環境防禦核心】** 若最高 Section 的 BM25 分數低於門檻值（例如 0.5），代表提問與知識庫無關。系統必須**立即中斷連線**並秒回拒絕回答，**絕對不調用 PostgreSQL 與 OpenAI**，以此幫系統擋掉 90% 以上的無效流量與 Token 成本。
* **事實撈取與短串流輸出（Short-Lived SSE）：** 檢索命中後，拿著 Top-K 的 Section ID 去 PostgreSQL 執行高速的主鍵（PK）查詢撈取原始內文。將事實文本封裝進 Grounded Prompt，呼叫大模型 API。利用 .NET 10 的 `IAsyncEnumerable<T>` 以 `text/event-stream` 格式將 Token 逐字推回前端。**當最後一個 Token 傳送完畢（包含 Sources 參考資料）後，服務主動關閉該次 HTTP 連線**，釋放所有記憶體與連線執行緒。
