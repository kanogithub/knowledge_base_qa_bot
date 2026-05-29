import React, { useState, useEffect, useRef } from 'react';
import { EventSourcePolyfill } from 'event-source-polyfill';
import Markdown from 'markdown-to-jsx';
import { 
  UploadCloud, 
  MessageSquare, 
  LogOut, 
  FileText, 
  CheckCircle, 
  Clock, 
  Send, 
  AlertCircle,
  FileCheck,
  Trash2
} from 'lucide-react';

interface TenantFile {
  fileName: string;
  fileSizeBytes: number;
  isIndexed: boolean;
  uploadedAt: string;
}

interface ChatMessage {
  sender: 'user' | 'ai';
  text: string;
  isStreaming?: boolean;
  sources?: Array<{ fileName: string; heading: string; score?: number; snippet?: string }>;
}


export default function App() {
  const [token, setToken] = useState<string | null>(localStorage.getItem('token'));
  const [username, setUsername] = useState<string>('');
  const [password, setPassword] = useState<string>('');
  const [loginError, setLoginError] = useState<string | null>(null);
  const [isRegistering, setIsRegistering] = useState<boolean>(false);
  const [confirmPassword, setConfirmPassword] = useState<string>('');
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  
  // Dashboard state
  const [user, setUser] = useState<string>('');
  const [files, setFiles] = useState<TenantFile[]>([]);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [query, setQuery] = useState('');
  const [isStreaming, setIsStreaming] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadStatus, setUploadStatus] = useState<string | null>(null);
  
  // Active citation details drawer
  const [activeCitation, setActiveCitation] = useState<{ fileName: string; heading: string; score?: number; snippet?: string } | null>(null);
  
  const chatEndRef = useRef<HTMLDivElement>(null);
  const sseRef = useRef<EventSourcePolyfill | null>(null);
  const lastScrollTimeRef = useRef<number>(0);

  // Decode username from JWT (simple client-side parse)
  useEffect(() => {
    if (token) {
      try {
        const payload = JSON.parse(atob(token.split('.')[1]));
        setUser(payload.user_id || 'User');
      } catch {
        setUser('User');
      }
    } else {
      setUser('');
    }
  }, [token]);

  // Load files list and subscribe to notifications
  useEffect(() => {
    if (token) {
      fetchFiles();
      setupNotificationStream();
    } else {
      if (sseRef.current) {
        sseRef.current.close();
        sseRef.current = null;
      }
      setFiles([]);
      setMessages([]);
    }
    return () => {
      if (sseRef.current) {
        sseRef.current.close();
      }
    };
  }, [token]);

  useEffect(() => {
    const now = Date.now();
    if (isStreaming) {
      if (now - lastScrollTimeRef.current > 250) {
        chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
        lastScrollTimeRef.current = now;
      }
    } else {
      chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
      lastScrollTimeRef.current = now;
    }
  }, [messages, isStreaming]);

  const fetchFiles = async () => {
    try {
      const res = await fetch('/api/index/files', {
        headers: { Authorization: `Bearer ${token}` }
      });
      if (res.ok) {
        const data = await res.json();
        setFiles(data);
      }
    } catch (err) {
      console.error('Failed to fetch files:', err);
    }
  };

  const setupNotificationStream = () => {
    if (sseRef.current) sseRef.current.close();

    const es = new EventSourcePolyfill('/api/notifications/stream', {
      headers: { Authorization: `Bearer ${token}` }
    });

    es.addEventListener('IndexCompleted', (e: any) => {
      const data = JSON.parse(e.data);
      setUploadStatus(`Success: ${data.message || 'File indexing completed.'}`);
      fetchFiles();
      setTimeout(() => setUploadStatus(null), 5000);
    });

    es.addEventListener('IndexFailed', (e: any) => {
      const data = JSON.parse(e.data);
      setUploadStatus(`Failed: ${data.message || 'File indexing failed.'}`);
      setTimeout(() => setUploadStatus(null), 5000);
    });

    es.addEventListener('IndexProcessing', (e: any) => {
      const data = JSON.parse(e.data);
      setUploadStatus(`Indexing: ${data.message || 'Processing file...'}`);
    });

    es.onerror = () => {
      console.error('SSE Notification connection error.');
    };

    sseRef.current = es;
  };

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoginError(null);
    setSuccessMessage(null);
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });
      
      if (res.ok) {
        const data = await res.json();
        localStorage.setItem('token', data.token);
        setToken(data.token);
      } else {
        setLoginError('Invalid username or password (try tenant-01/password)');
      }
    } catch {
      setLoginError('Network error connecting to Gateway.');
    }
  };

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoginError(null);
    setSuccessMessage(null);

    if (password.length < 6) {
      setLoginError('Password must be at least 6 characters long.');
      return;
    }

    if (password !== confirmPassword) {
      setLoginError('Passwords do not match.');
      return;
    }

    try {
      const res = await fetch('/api/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });

      if (res.status === 201) {
        setSuccessMessage('Registration successful! You can now sign in.');
        setIsRegistering(false);
        setConfirmPassword('');
        setPassword('');
      } else if (res.status === 409) {
        setLoginError('Username already exists.');
      } else {
        const errData = await res.json().catch(() => ({}));
        setLoginError(errData.message || 'Registration failed. Please try again.');
      }
    } catch {
      setLoginError('Network error connecting to Gateway.');
    }
  };

  const handleLogout = () => {
    localStorage.removeItem('token');
    setToken(null);
  };

  // Upload handler
  const handleUploadFiles = async (selectedFiles: FileList) => {
    setIsUploading(true);
    setUploadStatus('Uploading files...');
    const formData = new FormData();
    let hasValidFile = false;

    Array.from(selectedFiles).forEach(file => {
      if (file.name.endsWith('.md')) {
        formData.append('files', file);
        hasValidFile = true;
      }
    });

    if (!hasValidFile) {
      setUploadStatus('Error: Only Markdown (.md) files are accepted.');
      setIsUploading(false);
      setTimeout(() => setUploadStatus(null), 4000);
      return;
    }

    try {
      const res = await fetch('/api/index', {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
        body: formData
      });

      if (res.ok) {
        setUploadStatus('Enqueueing job... Waiting for indexer progress.');
        fetchFiles();
      } else {
        setUploadStatus('Failed to ingest files.');
        setTimeout(() => setUploadStatus(null), 4000);
      }
    } catch {
      setUploadStatus('Network error during file upload.');
      setTimeout(() => setUploadStatus(null), 4000);
    } finally {
      setIsUploading(false);
    }
  };

  const handleDeleteFile = async (fileName: string) => {
    if (!window.confirm(`Are you sure you want to delete "${fileName}"? This will also remove all its cited data from search indexes.`)) {
      return;
    }

    try {
      const res = await fetch(`/api/index/${encodeURIComponent(fileName)}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` }
      });

      if (res.ok) {
        setUploadStatus(`Success: File "${fileName}" deleted successfully.`);
        fetchFiles();
        setTimeout(() => setUploadStatus(null), 5000);
      } else {
        const errData = await res.json().catch(() => ({}));
        setUploadStatus(`Error: ${errData.message || 'Failed to delete file.'}`);
        setTimeout(() => setUploadStatus(null), 5000);
      }
    } catch {
      setUploadStatus('Error: Network error deleting file.');
      setTimeout(() => setUploadStatus(null), 5000);
    }
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      handleUploadFiles(e.dataTransfer.files);
    }
  };

  // Chat stream handler
  const handleSendChat = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim() || isStreaming) return;

    const userMessage = query;
    setQuery('');
    setMessages(prev => [...prev, { sender: 'user', text: userMessage }]);
    setIsStreaming(true);

    // Initial placeholder for streaming AI message
    setMessages(prev => [...prev, { sender: 'ai', text: '', isStreaming: true }]);

    try {
      const res = await fetch('/api/chat', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`
        },
        body: JSON.stringify({ query: userMessage })
      });

      if (!res.ok) {
        setMessages(prev => {
          const updated = [...prev];
          updated[updated.length - 1] = { sender: 'ai', text: 'Error generating response.' };
          return updated;
        });
        setIsStreaming(false);
        return;
      }

      const reader = res.body?.getReader();
      const decoder = new TextDecoder();
      let accumulatedText = '';
      let streamBuffer = '';

      if (reader) {
        while (true) {
          const { value, done } = await reader.read();
          if (done) break;

          streamBuffer += decoder.decode(value, { stream: true });
          const lines = streamBuffer.split('\n');
          streamBuffer = lines.pop() || '';

          for (const line of lines) {
            const trimmedLine = line.trim();
            if (trimmedLine.startsWith('data: ')) {
              try {
                const dataJson = JSON.parse(trimmedLine.slice(6));
                
                // Merge sources if present in current chunk
                if (dataJson.sources) {
                  setMessages(prev => {
                    const updated = [...prev];
                    const lastMsg = updated[updated.length - 1];
                    updated[updated.length - 1] = { 
                      ...lastMsg,
                      sources: dataJson.sources
                    };
                    return updated;
                  });
                }

                // Parse text chunks
                if (dataJson.text) {
                  accumulatedText += dataJson.text;
                  setMessages(prev => {
                    const updated = [...prev];
                    const lastMsg = updated[updated.length - 1];
                    updated[updated.length - 1] = { 
                      ...lastMsg,
                      text: accumulatedText,
                      isStreaming: true 
                    };
                    return updated;
                  });
                }
                
                // Final chunk handling
                if (dataJson.isFinal) {
                  setMessages(prev => {
                    const updated = [...prev];
                    const lastMsg = updated[updated.length - 1];
                    updated[updated.length - 1] = { 
                      ...lastMsg,
                      isStreaming: false
                    };
                    return updated;
                  });
                }
              } catch (err) {
                // Ignore chunk parse errors
              }
            }
          }
        }
        
        // Final flush of remaining buffer if any
        const finalTrimmed = streamBuffer.trim();
        if (finalTrimmed.startsWith('data: ')) {
          try {
            const dataJson = JSON.parse(finalTrimmed.slice(6));
            if (dataJson.text) {
              accumulatedText += dataJson.text;
              setMessages(prev => {
                const updated = [...prev];
                const lastMsg = updated[updated.length - 1];
                updated[updated.length - 1] = { 
                  ...lastMsg,
                  text: accumulatedText
                };
                return updated;
              });
            }
          } catch {}
        }
      }
    } catch {
      setMessages(prev => {
        const updated = [...prev];
        updated[updated.length - 1] = { sender: 'ai', text: 'Connection lost during stream.' };
        return updated;
      });
    } finally {
      setIsStreaming(false);
    }
  };

  // Click citation handler
  const handleCitationClick = (citationText: string, score?: number, snippet?: string) => {
    const decoded = decodeURIComponent(citationText);
    const parts = decoded.split('#');
    if (parts.length === 2) {
      setActiveCitation({ fileName: parts[0], heading: parts[1], score, snippet });
    }
  };

  // Helper to parse citations like [filename#heading] into markdown links
  const preprocessMarkdown = (text: string, sources?: Array<{ fileName: string; heading: string }>) => {
    // Regex matches [refund_policy.md#Refund Timeline]
    const citationRegex = /\[([a-zA-Z0-9_\-\.]+)#([^\]]+)\]/g;
    return text.replace(citationRegex, (_match, fileName, heading) => {
      const trimmedHeading = heading.trim();
      if (sources && sources.length > 0) {
        const idx = sources.findIndex(
          s => s.fileName.toLowerCase() === fileName.toLowerCase() &&
               (s.heading.toLowerCase() === trimmedHeading.toLowerCase() ||
                s.heading.toLowerCase().includes(trimmedHeading.toLowerCase()) ||
                trimmedHeading.toLowerCase().includes(s.heading.toLowerCase()))
        );
        if (idx !== -1) {
          const matchedSource = sources[idx];
          const rawLink = `${matchedSource.fileName}#${matchedSource.heading}`;
          return `[[${idx + 1}]](#citation:${encodeURIComponent(rawLink)})`;
        }
      }
      const rawLink = `${fileName}#${trimmedHeading}`;
      return `[${fileName}#${trimmedHeading}](#citation:${encodeURIComponent(rawLink)})`;
    });
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  // RENDER LOGIN CARD
  if (!token) {
    return (
      <div className="min-h-screen bg-slate-900 flex items-center justify-center p-4">
        <div className="max-w-md w-full bg-slate-800 rounded-2xl shadow-2xl border border-slate-700 p-8 space-y-6">
          <div className="text-center space-y-2">
            <div className="inline-flex p-3 bg-blue-500/10 text-blue-400 rounded-2xl">
              <FileCheck size={32} />
            </div>
            <h1 className="text-2xl font-bold text-white tracking-tight">
              {isRegistering ? 'Create Tenant Account' : 'Cloud-KB Portal'}
            </h1>
            <p className="text-sm text-slate-400">
              {isRegistering ? 'Sign up for a new workspace' : 'Multi-Tenant Knowledge Base Console'}
            </p>
          </div>

          <form onSubmit={isRegistering ? handleRegister : handleLogin} className="space-y-4">
            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">Username / Tenant ID</label>
              <input
                type="text"
                value={username}
                onChange={e => setUsername(e.target.value)}
                placeholder="e.g. tenant-01"
                className="w-full bg-slate-900 border border-slate-700 rounded-xl px-4 py-3 text-white placeholder-slate-500 focus:outline-none focus:border-blue-500 transition text-sm"
                required
              />
            </div>
            <div>
              <label className="block text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">Password</label>
              <input
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="••••••••"
                className="w-full bg-slate-900 border border-slate-700 rounded-xl px-4 py-3 text-white placeholder-slate-500 focus:outline-none focus:border-blue-500 transition text-sm"
                required
              />
            </div>

            {isRegistering && (
              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2">Confirm Password</label>
                <input
                  type="password"
                  value={confirmPassword}
                  onChange={e => setConfirmPassword(e.target.value)}
                  placeholder="••••••••"
                  className="w-full bg-slate-900 border border-slate-700 rounded-xl px-4 py-3 text-white placeholder-slate-500 focus:outline-none focus:border-blue-500 transition text-sm"
                  required
                />
              </div>
            )}

            {loginError && (
              <div className="bg-red-500/10 text-red-400 text-xs px-4 py-3 rounded-xl border border-red-500/20 flex items-center gap-2">
                <AlertCircle size={16} />
                <span>{loginError}</span>
              </div>
            )}

            {successMessage && (
              <div className="bg-emerald-500/10 text-emerald-400 text-xs px-4 py-3 rounded-xl border border-emerald-500/20 flex items-center gap-2">
                <CheckCircle size={16} />
                <span>{successMessage}</span>
              </div>
            )}

            <button
              type="submit"
              className="w-full bg-blue-600 hover:bg-blue-500 text-white rounded-xl py-3 font-semibold text-sm transition shadow-lg shadow-blue-600/20"
            >
              {isRegistering ? 'Sign Up' : 'Sign In'}
            </button>
          </form>

          <div className="text-center pt-2">
            <button
              type="button"
              onClick={() => {
                setIsRegistering(!isRegistering);
                setLoginError(null);
                setSuccessMessage(null);
                setPassword('');
                setConfirmPassword('');
              }}
              className="text-xs text-blue-400 hover:text-blue-300 font-semibold transition"
            >
              {isRegistering ? 'Already have an account? Sign In' : "Don't have an account? Sign Up"}
            </button>
          </div>
        </div>
      </div>
    );
  }

  // RENDER MAIN DASHBOARD
  return (
    <div className="h-screen bg-slate-950 text-slate-100 flex flex-col overflow-hidden">
      {/* HEADER */}
      <header className="border-b border-slate-800 bg-slate-900/50 backdrop-blur px-6 py-4 flex items-center justify-between sticky top-0 z-30">
        <div className="flex items-center gap-3">
          <div className="p-2 bg-blue-600 text-white rounded-lg">
            <FileText size={20} />
          </div>
          <div>
            <h1 className="text-lg font-bold text-white leading-none">Cloud-KB</h1>
            <p className="text-xs text-slate-400 mt-1">Tenant Space: <span className="font-mono text-blue-400 font-semibold">{user}</span></p>
          </div>
        </div>

        <button
          onClick={handleLogout}
          className="flex items-center gap-2 text-slate-400 hover:text-white px-3 py-2 rounded-lg hover:bg-slate-800 transition text-sm"
        >
          <LogOut size={16} />
          <span>Sign Out</span>
        </button>
      </header>

      {/* DASHBOARD BODY */}
      <main className="flex-grow max-w-[1600px] w-full mx-auto p-6 grid grid-cols-1 lg:grid-cols-12 gap-6 min-h-0 overflow-hidden">
        {/* LEFT COLUMN: FILE PANEL */}
        <div className="lg:col-span-5 flex flex-col space-y-6 h-full overflow-hidden">
          {/* UPLOAD ZONE */}
          <div
            onDragOver={handleDragOver}
            onDrop={handleDrop}
            className="border-2 border-dashed border-slate-800 hover:border-blue-500/50 bg-slate-900/30 hover:bg-slate-900/50 rounded-2xl p-6 text-center transition cursor-pointer relative group flex flex-col items-center justify-center space-y-3"
          >
            <input
              type="file"
              multiple
              accept=".md"
              onChange={e => e.target.files && handleUploadFiles(e.target.files)}
              className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
              disabled={isUploading}
            />
            <div className="p-4 bg-slate-800 rounded-2xl text-slate-400 group-hover:text-blue-400 transition">
              <UploadCloud size={32} />
            </div>
            <div>
              <p className="text-sm font-semibold text-slate-200">Drag & drop files here, or click to browse</p>
              <p className="text-xs text-slate-500 mt-1">Supports Markdown (.md) documents only</p>
            </div>
          </div>

          {/* STATUS NOTIFICATION TOAST */}
          {uploadStatus && (
            <div className={`px-4 py-3 rounded-xl border flex items-center gap-3 text-xs shadow-lg transition ${
              uploadStatus.startsWith('Error') || uploadStatus.startsWith('Failed')
                ? 'bg-red-500/10 border-red-500/20 text-red-400'
                : uploadStatus.startsWith('Success')
                ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400'
                : 'bg-blue-500/10 border-blue-500/20 text-blue-400'
            }`}>
              {uploadStatus.startsWith('Indexing') ? (
                <div className="animate-spin rounded-full h-4.5 w-4.5 border-2 border-blue-400 border-t-transparent" />
              ) : uploadStatus.startsWith('Success') ? (
                <CheckCircle size={16} />
              ) : (
                <AlertCircle size={16} />
              )}
              <span>{uploadStatus}</span>
            </div>
          )}

          {/* UPLOADED FILES LIST */}
          <div className="flex-1 bg-slate-900/50 border border-slate-800 rounded-2xl flex flex-col overflow-hidden">
            <div className="px-5 py-4 border-b border-slate-800 flex items-center justify-between">
              <h2 className="text-sm font-bold text-white flex items-center gap-2">
                <FileText size={16} className="text-slate-400" />
                <span>Knowledge Directory</span>
              </h2>
              <span className="text-[10px] font-bold bg-slate-800 px-2 py-1 rounded text-slate-400">{files.length} Files</span>
            </div>

            <div className="flex-1 overflow-y-auto">
              {files.length === 0 ? (
                <div className="h-full flex flex-col items-center justify-center text-slate-500 space-y-2 p-6">
                  <FileText size={28} className="opacity-50" />
                  <p className="text-xs">No documents uploaded in this space.</p>
                </div>
              ) : (
                <table className="w-full text-left border-collapse text-xs">
                  <thead>
                    <tr className="border-b border-slate-800 text-slate-500 font-semibold">
                      <th className="px-5 py-3">File Name</th>
                      <th className="px-5 py-3">Size</th>
                      <th className="px-5 py-3">Status</th>
                      <th className="px-5 py-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {files.map((file, idx) => (
                      <tr key={idx} className="border-b border-slate-850 hover:bg-slate-850/50 transition">
                        <td className="px-5 py-3 font-semibold text-slate-200 truncate max-w-[200px]" title={file.fileName}>
                          {file.fileName}
                        </td>
                        <td className="px-5 py-3 text-slate-400 font-mono">
                          {formatBytes(file.fileSizeBytes)}
                        </td>
                        <td className="px-5 py-3">
                          {file.isIndexed ? (
                            <span className="inline-flex items-center gap-1 bg-emerald-500/10 text-emerald-400 px-2 py-0.5 rounded font-medium text-[10px]">
                              <CheckCircle size={10} />
                              Indexed
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 bg-amber-500/10 text-amber-400 px-2 py-0.5 rounded font-medium text-[10px]">
                              <Clock size={10} />
                              Queued
                            </span>
                          )}
                        </td>
                        <td className="px-5 py-3 text-right">
                          <button
                            onClick={() => handleDeleteFile(file.fileName)}
                            className="text-slate-500 hover:text-red-400 p-1 rounded hover:bg-slate-800 transition inline-flex items-center justify-center"
                            title="Delete file"
                          >
                            <Trash2 size={14} />
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        </div>

        {/* RIGHT COLUMN: CHAT PANEL */}
        <div className="lg:col-span-7 flex flex-col bg-slate-900/50 border border-slate-800 rounded-2xl h-full overflow-hidden relative">
          {/* CHAT HEADER */}
          <div className="px-5 py-4 border-b border-slate-800 flex items-center justify-between">
            <h2 className="text-sm font-bold text-white flex items-center gap-2">
              <MessageSquare size={16} className="text-slate-400" />
              <span>Grounded QA Stream</span>
            </h2>
            {isStreaming && (
              <span className="text-[10px] bg-blue-500/10 text-blue-400 border border-blue-500/20 px-2.5 py-0.5 rounded-full flex items-center gap-1.5 animate-pulse font-medium">
                <span className="h-1.5 w-1.5 bg-blue-400 rounded-full" />
                AI is typing
              </span>
            )}
          </div>

          {/* MESSAGES CONTAINER */}
          <div className="flex-1 overflow-y-auto p-5 space-y-4">
            {messages.length === 0 ? (
              <div className="h-full flex flex-col items-center justify-center text-slate-500 space-y-3">
                <div className="p-4 bg-slate-800/50 rounded-full">
                  <MessageSquare size={36} className="opacity-50" />
                </div>
                <div className="text-center space-y-1">
                  <p className="text-sm font-bold text-slate-400">Ask your Knowledge Base</p>
                  <p className="text-xs text-slate-600 max-w-sm">Ask questions based on your indexed Markdown documents. The response will cite files used as sources.</p>
                </div>
              </div>
            ) : (
              messages.map((msg, idx) => (
                <div 
                  key={idx} 
                  className={`flex flex-col space-y-1.5 max-w-[85%] ${
                    msg.sender === 'user' ? 'ml-auto items-end' : 'mr-auto items-start'
                  }`}
                >
                  <span className="text-[10px] text-slate-500 uppercase tracking-wider font-bold">
                    {msg.sender === 'user' ? 'You' : 'Assistant'}
                  </span>
                  
                  <div className={`rounded-2xl px-4 py-3 text-sm leading-relaxed border ${
                    msg.sender === 'user'
                      ? 'bg-blue-600 border-blue-500 text-white rounded-tr-none'
                      : 'bg-slate-800 border-slate-750 text-slate-200 rounded-tl-none'
                  }`}>
                    {msg.sender === 'user' ? (
                      <p>{msg.text}</p>
                    ) : (
                      <div className="space-y-3">
                        <div className="prose prose-invert prose-xs max-w-none prose-p:my-1 prose-pre:my-1 prose-headings:text-white prose-a:text-blue-400">
                          <Markdown options={{
                            overrides: {
                              a: {
                                component: ({ children, href }: any) => {
                                  if (href && href.startsWith('#citation:')) {
                                    const rawCitation = href.replace('#citation:', '');
                                    const decoded = decodeURIComponent(rawCitation);
                                    const parts = decoded.split('#');
                                    let matchedScore: number | undefined;
                                    let matchedSnippet: string | undefined;
                                    if (parts.length === 2 && msg.sources) {
                                      const matched = msg.sources.find(
                                        s => s.fileName.toLowerCase() === parts[0].toLowerCase() &&
                                             (s.heading.toLowerCase() === parts[1].toLowerCase() ||
                                              s.heading.toLowerCase().includes(parts[1].toLowerCase()) ||
                                              parts[1].toLowerCase().includes(s.heading.toLowerCase()))
                                      );
                                      matchedScore = matched?.score;
                                      matchedSnippet = matched?.snippet;
                                    }
                                    return (
                                      <button
                                        onClick={() => handleCitationClick(rawCitation, matchedScore, matchedSnippet)}
                                        className="align-super text-[9px] bg-slate-750/80 hover:bg-slate-700 text-blue-400 hover:text-blue-350 px-1.5 py-0.5 rounded ml-0.5 transition font-semibold font-mono border border-slate-700/40"
                                      >
                                        {children}
                                      </button>
                                    );
                                  }
                                  return <a href={href}>{children}</a>;
                                }
                              }
                            }
                          }}>
                            {preprocessMarkdown(msg.text, msg.sources)}
                          </Markdown>
                          {msg.isStreaming && <span className="inline-block h-3.5 w-1.5 bg-slate-400 animate-pulse ml-0.5 align-middle" />}
                        </div>
                        {msg.sources && msg.sources.length > 0 && (
                          <div className="pt-2 border-t border-slate-700/50 space-y-1.5 text-xs text-slate-400">
                            <div className="font-semibold uppercase tracking-wider text-[10px] text-slate-500">Sources:</div>
                            <div className="flex flex-wrap items-start gap-1.5">
                              {msg.sources.map((src, sIdx) => (
                                <button
                                  key={sIdx}
                                  onClick={() => handleCitationClick(`${src.fileName}#${src.heading}`, src.score, src.snippet)}
                                  className="bg-slate-700/60 hover:bg-slate-700 text-slate-300 hover:text-white text-[11px] px-2.5 py-1 rounded transition font-mono border border-slate-650 flex items-start text-left gap-1.5 max-w-full break-all"
                                >
                                  <span className="text-blue-400 font-bold shrink-0">[{sIdx + 1}]</span>
                                  <span className="flex-1 min-w-0">
                                    <span>{src.fileName}#{src.heading}</span>
                                    {src.score !== undefined && (
                                      <span className="text-emerald-400 text-[10px] ml-1.5 font-semibold inline-block">
                                        (Score: {src.score.toFixed(2)})
                                      </span>
                                    )}
                                  </span>
                                </button>
                              ))}
                            </div>
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              ))
            )}
            <div ref={chatEndRef} />
          </div>

          {/* CHAT INPUT BAR */}
          <form onSubmit={handleSendChat} className="p-4 border-t border-slate-800 bg-slate-900/30 flex gap-2">
            <input
              type="text"
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="Ask a question..."
              className="flex-grow bg-slate-950 border border-slate-800 rounded-xl px-4 py-3 text-slate-200 placeholder-slate-500 focus:outline-none focus:border-blue-500 transition text-sm"
              disabled={isStreaming}
              required
            />
            <button
              type="submit"
              className="bg-blue-600 hover:bg-blue-500 text-white rounded-xl px-4 py-3 flex items-center justify-center transition disabled:opacity-50"
              disabled={isStreaming || !query.trim()}
            >
              <Send size={16} />
            </button>
          </form>

          {/* CITATIONS DETAILS BOTTOM DRAWER */}
          {activeCitation && (
            <div className="absolute inset-x-0 bottom-0 bg-slate-900 border-t border-slate-800 shadow-2xl p-5 z-40 animate-slide-up flex flex-col max-h-[40%] rounded-t-2xl">
              <div className="flex items-center justify-between border-b border-slate-800 pb-3 mb-3">
                <div className="flex items-center gap-2">
                  <FileText size={16} className="text-blue-400" />
                  <span className="text-xs font-bold text-white font-mono">{activeCitation.fileName}</span>
                  <span className="text-[10px] bg-slate-800 px-2 py-0.5 rounded text-slate-400">Heading: {activeCitation.heading}</span>
                </div>
                <button 
                  onClick={() => setActiveCitation(null)}
                  className="text-slate-400 hover:text-white text-xs px-2.5 py-1 rounded hover:bg-slate-800 transition"
                >
                  Close
                </button>
              </div>
              <div className="flex-1 overflow-y-auto text-xs text-slate-300 leading-relaxed font-mono bg-slate-950/60 p-3 rounded-lg border border-slate-850">
                {/* Find the cited section description in files/metadata (usually we fetch database top-k, this drawer shows citation identifier) */}
                <p>Citing source content section `#{activeCitation.heading}` from file `{activeCitation.fileName}`.</p>
                {activeCitation.score !== undefined && (
                  <p className="mt-1.5 text-emerald-400 font-semibold">BM25 Retrieval Score: {activeCitation.score.toFixed(4)}</p>
                )}
                {activeCitation.snippet && (
                  <div className="mt-3 p-3 bg-slate-900/80 border border-slate-800 rounded-lg text-slate-300 text-xs italic leading-relaxed whitespace-pre-wrap">
                    "{activeCitation.snippet}"
                  </div>
                )}
                <p className="mt-2 text-slate-500 text-[10px] italic">Note: Citations are highlighted dynamically in the chat dialogue above.</p>
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
