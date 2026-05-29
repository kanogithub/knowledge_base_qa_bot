## 📄 Feature 3: 多租戶 Markdown 知識編譯與顯式索引維護

* **功能群組：** 背景核心運算與持久層（Background Compute & Storage Layer）
* **實作元件：** `CloudKB.Worker.Indexer` + `PostgreSQL (EF Core 10)` + `Redis`

### 1. 功能概述

本功能為整個系統的「核心運算引擎」，負責實現 Andrej Karpathy 的 **LLM Wiki Pattern** 理念。透過背景 Worker 異步消費佇列任務，將非結構化的 Markdown 原始檔案，轉化為結構化的段落事實，並維護一套高效率的顯式索引地圖。

### 2. 核心技術規格

* **多租戶分布式鎖（Distributed Lock）：** 背景 Worker 開始處理特定租戶的任務前，必須先在 Redis 獲取分布式寫入鎖 `lock:index:{user_id}`（配置適當的 TTL 防死鎖），確保同租戶並發寫入時的互斥性，防止詞頻統計被覆蓋錯亂。
* **知識段落編譯（Compilation）：** 從 S3 讀取檔案，執行 Line-by-Line 樹狀解析。依據 Heading（`#` 至 `######`）將文件切分成獨立的 `TenantSection`，保留標題麵包屑路徑（HeadingPath）。利用 .NET 10 的 `SearchValues<string>` 進行高速分詞與停用詞過濾，並透過 **EF Core 10 的 Bulk Operations（批量寫入）** 倒入 PostgreSQL。
* **顯式索引地圖刷新：** 在背景記憶體內重新計算該租戶專屬的 BM25 統計指標（Document Frequency, Average Document Length）。更新完成後，將全新、不含重型內文的輕量化 `TenantKbIndex` 序列化為 JSON 刷入 Redis 快取（Key: `kb:index:{user_id}`），隨後釋放分布式鎖。
