import React, { useState, useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';

interface UserInfo {
  username: string;
  fullName: string;
  role: string;
  accessToken: string;
  expiresAtUnix: number;
}

interface NotificationItem {
  eventId: string;
  eventType: string;
  occurredAtUtc: string;
  entityId: string;
  message: string;
}

export default function App() {
  const [user, setUser] = useState<UserInfo | null>(() => {
    const saved = localStorage.getItem('digisme_user');
    return saved ? JSON.parse(saved) : null;
  });

  const [usernameInput, setUsernameInput] = useState('admin');
  const [passwordInput, setPasswordInput] = useState('admin123');
  const [loginError, setLoginError] = useState('');
  const [loading, setLoading] = useState(false);

  // SignalR state
  const [connectionStatus, setConnectionStatus] = useState<'Disconnected' | 'Connecting' | 'Connected' | 'Reconnecting'>('Disconnected');
  const [notifications, setNotifications] = useState<NotificationItem[]>([]);
  const [filterType, setFilterType] = useState<string>('ALL');

  // Diagnostic states
  const [healthData, setHealthData] = useState<any>(null);
  const [barcodeInput, setBarcodeInput] = useState('000000001');
  const [barcodeResult, setBarcodeResult] = useState<any>(null);
  const [barcodeLoading, setBarcodeLoading] = useState(false);
  const [currentShift, setCurrentShift] = useState<any>(null);

  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (user) {
      localStorage.setItem('digisme_user', JSON.stringify(user));
      connectSignalR(user.accessToken);
      fetchHealth();
      fetchCurrentShift(user.accessToken);
    } else {
      localStorage.removeItem('digisme_user');
      if (connectionRef.current) {
        connectionRef.current.stop();
        connectionRef.current = null;
      }
      setConnectionStatus('Disconnected');
    }

    return () => {
      if (connectionRef.current) {
        connectionRef.current.stop();
      }
    };
  }, [user]);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoginError('');
    setLoading(true);
    try {
      const res = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: usernameInput, password: passwordInput })
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.error || `Đăng nhập thất bại (HTTP ${res.status})`);
      }
      const data = await res.json();
      setUser(data);
    } catch (err: any) {
      setLoginError(err.message || 'Lỗi kết nối đến server');
    } finally {
      setLoading(false);
    }
  };

  const handleLogout = () => {
    setUser(null);
    setNotifications([]);
  };

  const connectSignalR = (token: string) => {
    if (connectionRef.current) {
      connectionRef.current.stop();
    }

    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/pos', {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    connectionRef.current = newConnection;

    newConnection.onreconnecting(() => setConnectionStatus('Reconnecting'));
    newConnection.onreconnected(() => setConnectionStatus('Connected'));
    newConnection.onclose(() => setConnectionStatus('Disconnected'));

    newConnection.on('ReceiveNotification', (notification: NotificationItem) => {
      setNotifications(prev => [notification, ...prev.slice(0, 99)]);
    });

    setConnectionStatus('Connecting');
    newConnection.start()
      .then(() => {
        setConnectionStatus('Connected');
        console.log('SignalR connected successfully to /hubs/pos');
      })
      .catch(err => {
        console.error('SignalR connection failed: ', err);
        setConnectionStatus('Disconnected');
      });
  };

  const fetchHealth = async () => {
    try {
      const res = await fetch('/api/v1/health/ready');
      if (res.ok) {
        const data = await res.json();
        setHealthData(data);
      }
    } catch (e) {
      console.error(e);
    }
  };

  const fetchCurrentShift = async (token: string) => {
    try {
      const res = await fetch('/api/v1/shifts/current', {
        headers: { 'Authorization': `Bearer ${token}` }
      });
      if (res.ok) {
        const data = await res.json();
        setCurrentShift(data);
      } else {
        setCurrentShift(null);
      }
    } catch (e) {
      setCurrentShift(null);
    }
  };

  const handleScanBarcode = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!barcodeInput.trim()) return;
    setBarcodeLoading(true);
    setBarcodeResult(null);
    try {
      const res = await fetch(`/api/v1/products/barcode/${barcodeInput.trim()}`);
      if (!res.ok) {
        throw new Error(`Không tìm thấy sản phẩm hoặc lỗi (${res.status})`);
      }
      const data = await res.json();
      setBarcodeResult(data);
    } catch (err: any) {
      setBarcodeResult({ error: err.message });
    } finally {
      setBarcodeLoading(false);
    }
  };

  const filteredNotifications = notifications.filter(n => {
    if (filterType === 'ALL') return true;
    return n.eventType === filterType;
  });

  const getEventBadgeColor = (type: string) => {
    switch (type) {
      case 'OrderCreated': return 'bg-blue-500/20 text-blue-400 border-blue-500/30';
      case 'OrderCompleted': return 'bg-emerald-500/20 text-emerald-400 border-emerald-500/30';
      case 'PaymentConfirmed': return 'bg-purple-500/20 text-purple-400 border-purple-500/30';
      case 'LowStockAlert': return 'bg-amber-500/20 text-amber-400 border-amber-500/30';
      case 'ShiftOpened': return 'bg-cyan-500/20 text-cyan-400 border-cyan-500/30';
      case 'ShiftClosed': return 'bg-rose-500/20 text-rose-400 border-rose-500/30';
      default: return 'bg-slate-700 text-slate-300 border-slate-600';
    }
  };

  if (!user) {
    return (
      <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center p-4">
        <div className="max-w-md w-full bg-slate-900 border border-slate-800 rounded-2xl shadow-2xl p-8">
          <div className="text-center mb-8">
            <span className="inline-block px-3 py-1 bg-indigo-500/10 text-indigo-400 text-xs font-semibold rounded-full border border-indigo-500/20 mb-3">
              Sprint 4 SignalR & Realtime
            </span>
            <h1 className="text-2xl font-bold text-white tracking-tight">DigiSME POS Client</h1>
            <p className="text-sm text-slate-400 mt-1">Đăng nhập để kết nối SignalR Hub & Quản lý bán hàng</p>
          </div>

          {loginError && (
            <div className="mb-6 p-4 bg-rose-500/10 border border-rose-500/30 rounded-xl text-rose-400 text-sm">
              {loginError}
            </div>
          )}

          <form onSubmit={handleLogin} className="space-y-4">
            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1">Tài khoản</label>
              <input
                type="text"
                value={usernameInput}
                onChange={e => setUsernameInput(e.target.value)}
                required
                className="w-full px-4 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-white text-sm focus:outline-none focus:border-indigo-500 transition-colors"
                placeholder="admin hoặc cau1"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1">Mật khẩu</label>
              <input
                type="password"
                value={passwordInput}
                onChange={e => setPasswordInput(e.target.value)}
                required
                className="w-full px-4 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-white text-sm focus:outline-none focus:border-indigo-500 transition-colors"
                placeholder="••••••••"
              />
            </div>
            <button
              type="submit"
              disabled={loading}
              className="w-full py-3 bg-indigo-600 hover:bg-indigo-500 active:bg-indigo-700 text-white font-medium rounded-xl text-sm transition-all shadow-lg shadow-indigo-600/20 disabled:opacity-50"
            >
              {loading ? 'Đang đăng nhập...' : 'Đăng nhập hệ thống'}
            </button>
          </form>

          <div className="mt-8 pt-6 border-t border-slate-800 text-xs text-slate-400 space-y-2">
            <p className="font-medium text-slate-300">Tài khoản Seed (Development):</p>
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => { setUsernameInput('admin'); setPasswordInput('admin123'); }}
                className="p-2 bg-slate-800/80 hover:bg-slate-800 border border-slate-700/60 rounded-lg text-left transition-colors"
              >
                <span className="font-semibold text-indigo-400 block">admin</span>
                <span className="text-[10px] text-slate-400">Manager (Toàn quyền)</span>
              </button>
              <button
                type="button"
                onClick={() => { setUsernameInput('cau1'); setPasswordInput('cau11234'); }}
                className="p-2 bg-slate-800/80 hover:bg-slate-800 border border-slate-700/60 rounded-lg text-left transition-colors"
              >
                <span className="font-semibold text-emerald-400 block">cau1</span>
                <span className="text-[10px] text-slate-400">Cashier (Thu ngân)</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col">
      {/* Top Navbar */}
      <header className="bg-slate-900 border-b border-slate-800 px-6 py-4 flex items-center justify-between sticky top-0 z-50">
        <div className="flex items-center space-x-4">
          <div className="flex items-center space-x-2">
            <div className="w-3 h-3 rounded-full bg-indigo-500 animate-pulse"></div>
            <span className="font-bold text-lg text-white tracking-tight">DigiSME POS</span>
          </div>
          <span className="text-xs px-2.5 py-1 bg-slate-800 text-slate-300 rounded-md border border-slate-700">
            Sprint 4 Realtime Hub
          </span>
        </div>

        <div className="flex items-center space-x-6">
          {/* Connection Badge */}
          <div className="flex items-center space-x-2 bg-slate-800/80 px-3 py-1.5 rounded-full border border-slate-700/80 text-xs">
            <span className={`w-2.5 h-2.5 rounded-full ${
              connectionStatus === 'Connected' ? 'bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]' :
              connectionStatus === 'Connecting' || connectionStatus === 'Reconnecting' ? 'bg-amber-500 animate-ping' : 'bg-rose-500'
            }`} />
            <span className="text-slate-300 font-medium">SignalR: {connectionStatus}</span>
          </div>

          <div className="flex items-center space-x-3 pl-4 border-l border-slate-800">
            <div className="text-right">
              <div className="text-sm font-semibold text-white">{user.fullName || user.username}</div>
              <div className="text-[11px] text-indigo-400 font-medium uppercase tracking-wider">{user.role}</div>
            </div>
            <button
              onClick={handleLogout}
              className="px-3 py-1.5 bg-slate-800 hover:bg-rose-500/20 hover:text-rose-400 text-slate-300 rounded-lg text-xs font-medium border border-slate-700 transition-all"
            >
              Đăng xuất
            </button>
          </div>
        </div>
      </header>

      {/* Main Content Dashboard */}
      <main className="flex-1 p-6 grid grid-cols-1 lg:grid-cols-3 gap-6 max-w-7xl mx-auto w-full">
        {/* Left Column: Diagnostics & Shift */}
        <div className="space-y-6">
          {/* Health Status Card */}
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-lg">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-sm font-semibold text-white uppercase tracking-wider">Trạng thái Hệ thống</h2>
              <button
                onClick={fetchHealth}
                className="text-xs text-indigo-400 hover:text-indigo-300 transition-colors"
              >
                Làm mới
              </button>
            </div>
            {healthData ? (
              <div className="space-y-3 text-xs">
                <div className="flex justify-between py-1.5 border-b border-slate-800">
                  <span className="text-slate-400">Trạng thái chung:</span>
                  <span className={`font-semibold ${healthData.status === 'ready' ? 'text-emerald-400' : 'text-amber-400'}`}>
                    {healthData.status.toUpperCase()}
                  </span>
                </div>
                <div className="flex justify-between py-1.5 border-b border-slate-800">
                  <span className="text-slate-400">Database (PostgreSQL):</span>
                  <span className={`font-semibold ${healthData.database ? 'text-emerald-400' : 'text-rose-400'}`}>
                    {healthData.database ? 'Connected' : 'Disconnected'}
                  </span>
                </div>
                <div className="flex justify-between py-1.5">
                  <span className="text-slate-400">AI Service (FastAPI):</span>
                  <span className={`font-semibold ${healthData.aiService ? 'text-emerald-400' : 'text-amber-400'}`}>
                    {healthData.aiService ? 'Available' : 'Unavailable (Mock)'}
                  </span>
                </div>
              </div>
            ) : (
              <p className="text-xs text-slate-500">Đang tải trạng thái...</p>
            )}
          </div>

          {/* Current Shift Card */}
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-lg">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-sm font-semibold text-white uppercase tracking-wider">Ca làm việc hiện tại</h2>
              <button
                onClick={() => fetchCurrentShift(user.accessToken)}
                className="text-xs text-indigo-400 hover:text-indigo-300 transition-colors"
              >
                Kiểm tra
              </button>
            </div>
            {currentShift ? (
              <div className="space-y-2.5 text-xs">
                <div className="flex justify-between">
                  <span className="text-slate-400">Mã ca:</span>
                  <span className="font-semibold text-white">#{currentShift.id}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400">Nhân viên:</span>
                  <span className="text-slate-200">{currentShift.username}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400">Thời gian mở:</span>
                  <span className="text-slate-200">{new Date(currentShift.startedAt).toLocaleTimeString()}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400">Tiền đầu ca:</span>
                  <span className="font-semibold text-emerald-400">{currentShift.openingCash.toLocaleString()} đ</span>
                </div>
              </div>
            ) : (
              <div className="text-center py-4 text-xs text-slate-400">
                Chưa có ca làm việc nào đang mở.
              </div>
            )}
          </div>

          {/* Barcode Quick Scan Tool */}
          <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 shadow-lg">
            <h2 className="text-sm font-semibold text-white uppercase tracking-wider mb-3">Tra cứu Barcode (F3)</h2>
            <form onSubmit={handleScanBarcode} className="space-y-3">
              <div className="flex space-x-2">
                <input
                  type="text"
                  value={barcodeInput}
                  onChange={e => setBarcodeInput(e.target.value)}
                  placeholder="Nhập barcode (vd: 000000001)"
                  className="flex-1 px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-white text-xs focus:outline-none focus:border-indigo-500"
                />
                <button
                  type="submit"
                  disabled={barcodeLoading}
                  className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white rounded-xl text-xs font-medium transition-all"
                >
                  {barcodeLoading ? '...' : 'Tra cứu'}
                </button>
              </div>
            </form>

            {barcodeResult && (
              <div className="mt-4 p-3 bg-slate-800/60 border border-slate-700 rounded-xl text-xs space-y-1.5">
                {barcodeResult.error ? (
                  <p className="text-rose-400">{barcodeResult.error}</p>
                ) : (
                  <>
                    <div className="flex justify-between font-semibold text-white">
                      <span>{barcodeResult.name}</span>
                      <span className="text-emerald-400">{barcodeResult.retailPrice?.toLocaleString()} đ</span>
                    </div>
                    <div className="text-slate-400 text-[11px] flex justify-between">
                      <span>Barcode: {barcodeResult.barcode}</span>
                      <span>Tồn: <strong className={barcodeResult.belowMinStock ? 'text-amber-400' : 'text-white'}>{barcodeResult.stock} {barcodeResult.baseUnit}</strong></span>
                    </div>
                  </>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Right 2 Columns: Realtime Notification Feed */}
        <div className="lg:col-span-2 bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg flex flex-col h-[calc(100vh-7rem)]">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-slate-800 gap-3">
            <div>
              <h2 className="text-base font-bold text-white flex items-center space-x-2">
                <span>Dòng Sự Kiện Realtime (SignalR)</span>
                <span className="px-2 py-0.5 bg-indigo-500/20 text-indigo-400 rounded-full text-xs font-semibold border border-indigo-500/30">
                  {notifications.length}
                </span>
              </h2>
              <p className="text-xs text-slate-400 mt-0.5">Nhận thông báo sự kiện tức thì từ Hub <code className="text-indigo-300">/hubs/pos</code></p>
            </div>

            <div className="flex items-center space-x-2">
              <select
                value={filterType}
                onChange={e => setFilterType(e.target.value)}
                className="bg-slate-800 border border-slate-700 text-slate-200 text-xs rounded-xl px-3 py-1.5 focus:outline-none focus:border-indigo-500"
              >
                <option value="ALL">Tất cả sự kiện</option>
                <option value="OrderCreated">OrderCreated</option>
                <option value="OrderCompleted">OrderCompleted</option>
                <option value="PaymentConfirmed">PaymentConfirmed</option>
                <option value="LowStockAlert">LowStockAlert</option>
                <option value="ShiftOpened">ShiftOpened</option>
                <option value="ShiftClosed">ShiftClosed</option>
              </select>

              <button
                onClick={() => setNotifications([])}
                className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-xs font-medium border border-slate-700 transition-all"
              >
                Xóa bảng
              </button>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto mt-4 space-y-3 pr-1">
            {filteredNotifications.length === 0 ? (
              <div className="h-full flex flex-col items-center justify-center text-slate-500 text-center p-8 space-y-2">
                <svg className="w-12 h-12 text-slate-700 stroke-1" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
                </svg>
                <p className="text-sm font-medium text-slate-400">Chưa có sự kiện realtime nào.</p>
                <p className="text-xs text-slate-500 max-w-xs">
                  Khi có tạo đơn hàng, xác nhận thanh toán VietQR, mở/đóng ca hoặc cảnh báo tồn kho từ Backend, thông báo sẽ xuất hiện ngay lập tức tại đây.
                </p>
              </div>
            ) : (
              filteredNotifications.map((n, idx) => (
                <div
                  key={n.eventId || idx}
                  className="bg-slate-800/70 border border-slate-700/60 rounded-xl p-4 transition-all hover:border-slate-600"
                >
                  <div className="flex items-center justify-between mb-2">
                    <span className={`px-2.5 py-0.5 rounded-md text-[11px] font-semibold border ${getEventBadgeColor(n.eventType)}`}>
                      {n.eventType}
                    </span>
                    <span className="text-[11px] text-slate-400">
                      {new Date(n.occurredAtUtc).toLocaleTimeString()}
                    </span>
                  </div>
                  <p className="text-sm text-slate-100 font-medium">{n.message}</p>
                  <div className="mt-2 text-[10px] text-slate-400 flex items-center space-x-3">
                    <span>Entity ID: <strong className="text-slate-300">{n.entityId}</strong></span>
                    <span>Event ID: <strong className="text-slate-300">{n.eventId.substring(0, 8)}...</strong></span>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      </main>
    </div>
  );
}
