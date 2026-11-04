import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, NavLink, Route, Routes, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarDays, CheckCircle2, Clock3, MapPin, Menu, QrCode, ShieldCheck, TicketCheck, X } from 'lucide-react'

type EventSummary = { id: string; slug: string; title: string; location: string; category: string; startsAt: string; timeZone: string; fromPriceKobo: number; available: number }
type TicketTier = { id: string; name: string; priceKobo: number; currency: string; available: number; salesStart: string; salesEnd: string }
type EventDetail = EventSummary & { description: string; endsAt: string; ticketTypes: TicketTier[] }
type User = { id: string; email: string; displayName: string }
type Order = { id: string; eventTitle: string; status: string; amountKobo: number; currency: string; expiresAt: string; ticketIds: string[] }
type TicketPass = { id: string; eventTitle: string; location: string; startsAt: string; timeZone: string; tier: string; qrData: string; qrImage: string }
type OrganizerEvent = { id: string; slug: string; title: string; status: string; startsAt: string; sold: number; capacity: number }

let antiforgeryToken = ''
async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const method = (init?.method ?? 'GET').toUpperCase()
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method) && !antiforgeryToken) {
    const tokenResponse = await fetch('/api/v1/auth/antiforgery', { credentials: 'include' })
    antiforgeryToken = (await tokenResponse.json()).token
  }
  const response = await fetch(path, { credentials: 'include', ...init, headers: {
    ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
    ...(!['GET', 'HEAD', 'OPTIONS'].includes(method) ? { 'X-XSRF-TOKEN': antiforgeryToken } : {}),
    ...init?.headers,
  } })
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.detail ?? body?.title ?? `Request failed (${response.status})`)
  }
  if (response.status === 204) return undefined as T
  return response.json()
}
const money = (kobo: number) => new Intl.NumberFormat('en-NG', { style: 'currency', currency: 'NGN', maximumFractionDigits: 0 }).format(kobo / 100)
const date = (value: string, zone?: string) => new Intl.DateTimeFormat('en-NG', { dateStyle: 'medium', timeStyle: 'short', timeZone: zone }).format(new Date(value))

function App() {
  const client = useQueryClient()
  const me = useQuery({ queryKey: ['me'], queryFn: () => api<User>('/api/v1/auth/me'), retry: false })
  const [menu, setMenu] = useState(false)
  const logout = useMutation({ mutationFn: () => api('/api/v1/auth/logout', { method: 'POST' }), onSuccess: () => {
    antiforgeryToken = ''
    client.setQueryData(['me'], undefined)
    client.invalidateQueries({ queryKey: ['me'] })
  } })
  return <div className="app-shell">
    <header className="site-header"><Link to="/" className="brand"><span className="brand-mark"><TicketCheck /></span>GateMint</Link>
      <button className="menu-button" aria-label="Toggle navigation" onClick={() => setMenu(value => !value)}>{menu ? <X /> : <Menu />}</button>
      <nav className={menu ? 'nav open' : 'nav'} aria-label="Primary navigation">
        <NavLink to="/">Discover</NavLink>{me.data ? <><NavLink to="/orders">My tickets</NavLink><NavLink to="/organizer">Organize</NavLink><NavLink to="/scanner">Scan</NavLink>
          <button className="nav-quiet" onClick={() => logout.mutate()}>Sign out</button></> : <NavLink to="/auth">Sign in</NavLink>}
      </nav>
    </header>
    <main><Routes>
      <Route path="/" element={<Discovery />} /><Route path="/events/:slug" element={<EventPage user={me.data} />} />
      <Route path="/auth" element={<AuthPage />} /><Route path="/orders" element={<OrdersPage />} />
      <Route path="/tickets/:id" element={<TicketPage />} /><Route path="/organizer" element={<OrganizerPage />} />
      <Route path="/scanner" element={<ScannerPage />} /><Route path="/simulator" element={<SimulatorPage />} />
      <Route path="/payment-return" element={<PaymentReturn />} /><Route path="*" element={<NotFound />} />
    </Routes></main>
    <footer><span className="brand compact"><span className="brand-mark"><TicketCheck /></span>GateMint</span><p>From booking to the gate.</p><p>Test payments only · Built for NGN events</p></footer>
  </div>
}

function Discovery() {
  const [search, setSearch] = useState('')
  const events = useQuery({ queryKey: ['events', search], queryFn: () => api<{items: EventSummary[]}>(`/api/v1/events?search=${encodeURIComponent(search)}`) })
  return <><section className="hero"><div className="eyebrow"><span /> Discover what moves Lagos</div><h1>Good plans deserve<br/><em>a smooth entrance.</em></h1>
    <p>Find thoughtful gatherings, reserve securely, and arrive with one ticket that simply works.</p>
    <div className="search"><label htmlFor="search">Find an event</label><input id="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Music, food, design…" /></div>
    <div className="hero-proof"><span><ShieldCheck /> Protected reservations</span><span><QrCode /> Instant digital passes</span></div></section>
    <section className="section"><div className="section-heading"><div><span className="kicker">Upcoming</span><h2>Worth showing up for</h2></div><p>Curated experiences across the city.</p></div>
      {events.isPending ? <CardsSkeleton /> : events.isError ? <ErrorState message={events.error.message} /> : events.data.items.length === 0 ? <Empty title="No events found" body="Try a broader search." /> :
        <div className="event-grid">{events.data.items.map((event, index) => <EventCard key={event.id} event={event} index={index} />)}</div>}
    </section></>
}

function EventCard({ event, index }: { event: EventSummary; index: number }) {
  return <Link to={`/events/${event.slug}`} className={`event-card art-${index % 3}`}><div className="event-art"><span>{event.category}</span><strong>{new Date(event.startsAt).getDate()}</strong><small>{new Intl.DateTimeFormat('en', { month: 'short' }).format(new Date(event.startsAt))}</small></div>
    <div className="event-card-body"><span className="category">{event.category}</span><h3>{event.title}</h3><p><MapPin /> {event.location}</p><p><CalendarDays /> {date(event.startsAt, event.timeZone)}</p>
      <div className="event-price"><span>From <strong>{money(event.fromPriceKobo)}</strong></span><span>{event.available} left</span></div></div></Link>
}

function EventPage({ user }: { user?: User }) {
  const { slug } = useParams(); const navigate = useNavigate(); const [quantity, setQuantity] = useState(1); const [tierId, setTierId] = useState('')
  const event = useQuery({ queryKey: ['event', slug], queryFn: () => api<EventDetail>(`/api/v1/events/${slug}`) })
  const selectedTierId = tierId || event.data?.ticketTypes[0]?.id || ''
  const reserve = useMutation({ mutationFn: () => api<{orderId: string}>('/api/v1/orders', { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ ticketTypeId: selectedTierId, quantity }) }),
    onSuccess: data => navigate(`/orders?pay=${data.orderId}`) })
  if (event.isPending) return <PageLoading />; if (event.isError) return <ErrorState message={event.error.message} />
  const item = event.data
  return <section className="event-detail"><div className="event-banner"><span>{item.category}</span><h1>{item.title}</h1><p>{item.location}</p></div>
    <div className="detail-layout"><article><div className="kicker">The experience</div><h2>A date to keep</h2><p className="lead">{item.description}</p>
      <div className="facts"><div><CalendarDays/><span><strong>{date(item.startsAt, item.timeZone)}</strong><small>{item.timeZone}</small></span></div><div><MapPin/><span><strong>{item.location}</strong><small>Doors open before start time</small></span></div></div></article>
      <aside className="booking-card"><div className="test-badge">Test payment experience</div><h2>Choose your pass</h2>
        <label htmlFor="tier">Ticket tier</label><select id="tier" value={selectedTierId} onChange={e => setTierId(e.target.value)}>{item.ticketTypes.map(tier => <option key={tier.id} value={tier.id}>{tier.name} — {money(tier.priceKobo)} ({tier.available} left)</option>)}</select>
        <label htmlFor="quantity">Quantity</label><select id="quantity" value={quantity} onChange={e => setQuantity(Number(e.target.value))}>{[1,2,3,4,5,6].map(value => <option key={value}>{value}</option>)}</select>
        {user ? <button className="primary" disabled={reserve.isPending || !selectedTierId} onClick={() => reserve.mutate()}>{reserve.isPending ? 'Holding tickets…' : 'Reserve for 10 minutes'}</button> : <Link className="button primary" to="/auth">Sign in to reserve</Link>}
        {reserve.isError ? <p className="form-error" role="alert">{reserve.error.message}</p> : null}<small>Prices are calculated securely by GateMint. Your hold expires after 10 minutes.</small></aside>
    </div></section>
}

function AuthPage() {
  const [mode, setMode] = useState<'login'|'register'|'reset'>('login'); const navigate = useNavigate(); const client = useQueryClient()
  const submit = useMutation({ mutationFn: ({ form }: { form: HTMLFormElement }) => {
    const values = Object.fromEntries(new FormData(form)); const endpoint = mode === 'login' ? 'login' : mode === 'register' ? 'register' : 'password-reset'
    return api(`/api/v1/auth/${endpoint}`, { method: 'POST', body: JSON.stringify(values) }) },
    onSuccess: () => { if (mode === 'reset') setMode('login'); else {
      antiforgeryToken = ''
      client.invalidateQueries({ queryKey: ['me'] })
      navigate('/')
    } } })
  const onSubmit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); submit.mutate({ form: event.currentTarget }) }
  return <section className="auth-page"><div className="auth-copy"><span className="kicker">Your next great night</span><h1>One account.<br/>Every doorway.</h1><p>Book confidently, keep every pass close, and move through the gate without the usual friction.</p></div>
    <form className="panel form" onSubmit={onSubmit}><div className="test-badge">Secure session · no browser tokens</div><h2>{mode === 'login' ? 'Welcome back' : mode === 'register' ? 'Create your account' : 'Reset your password'}</h2>
      {mode === 'register' ? <label>Full name<input name="displayName" autoComplete="name" required minLength={2}/></label> : null}
      <label>Email address<input name="email" type="email" autoComplete="email" required/></label>
      {mode !== 'reset' ? <label>Password<input name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required minLength={10}/></label> : null}
      <button className="primary" disabled={submit.isPending}>{submit.isPending ? 'Please wait…' : mode === 'login' ? 'Sign in' : mode === 'register' ? 'Create account' : 'Send reset instructions'}</button>
      {submit.isError ? <p className="form-error" role="alert">{submit.error.message}</p> : null}
      <div className="form-links">{mode !== 'login' ? <button type="button" onClick={() => setMode('login')}>Sign in</button> : <><button type="button" onClick={() => setMode('register')}>Create account</button><button type="button" onClick={() => setMode('reset')}>Forgot password?</button></>}</div></form></section>
}

function OrdersPage() {
  const [params, setParams] = useSearchParams(); const client = useQueryClient(); const orders = useQuery({ queryKey: ['orders'], queryFn: () => api<{items: Order[]}>('/api/v1/orders'), refetchInterval: 5000 })
  const payment = useMutation({ mutationFn: (id: string) => api<{authorizationUrl: string}>(`/api/v1/orders/${id}/payments`, { method: 'POST' }), onSuccess: data => { window.location.href = data.authorizationUrl } })
  useEffect(() => { const id = params.get('pay'); if (id && orders.data && !payment.isPending) { setParams({}); payment.mutate(id) } }, [orders.data, params, payment, setParams])
  const cancel = useMutation({ mutationFn: (id: string) => api(`/api/v1/orders/${id}/cancel`, { method: 'POST' }), onSuccess: () => client.invalidateQueries({ queryKey: ['orders'] }) })
  return <section className="section page"><div className="section-heading"><div><span className="kicker">Your plans</span><h1>Orders & tickets</h1></div><p>Pending payments stay pending until the provider confirms them.</p></div>
    {orders.isPending ? <PageLoading/> : orders.isError ? <ErrorState message={orders.error.message}/> : orders.data.items.length === 0 ? <Empty title="No orders yet" body="The city is waiting."/> :
      <div className="order-list">{orders.data.items.map(order => <article className="order-row" key={order.id}><div><span className={`status ${order.status.toLowerCase()}`}>{order.status}</span><h3>{order.eventTitle}</h3><p>{money(order.amountKobo)} · Hold ends {date(order.expiresAt)}</p></div><div className="order-actions">{order.status === 'AwaitingPayment' ? <><button className="primary small" onClick={() => payment.mutate(order.id)}>Pay securely</button><button className="secondary small" onClick={() => cancel.mutate(order.id)}>Cancel</button></> : null}{order.ticketIds.map((id, index) => <Link className="button secondary small" to={`/tickets/${id}`} key={id}>Ticket {index + 1}</Link>)}</div></article>)}</div>}
    {payment.isError ? <p className="form-error">{payment.error.message}</p> : null}</section>
}

function TicketPage() {
  const { id } = useParams(); const ticket = useQuery({ queryKey: ['ticket', id], queryFn: () => api<TicketPass>(`/api/v1/tickets/${id}`) })
  if (ticket.isPending) return <PageLoading/>; if (ticket.isError) return <ErrorState message={ticket.error.message}/>
  const pass = ticket.data
  return <section className="ticket-page"><div className="ticket-pass"><div className="pass-top"><span className="brand compact"><span className="brand-mark"><TicketCheck /></span>GateMint</span><span className="status confirmed">Active</span></div><div className="pass-art"><span>ADMIT</span><strong>01</strong></div>
    <div className="pass-body"><span className="kicker">{pass.tier}</span><h1>{pass.eventTitle}</h1><p><CalendarDays/> {date(pass.startsAt, pass.timeZone)}</p><p><MapPin/> {pass.location}</p></div><div className="perforation"/><div className="qr-wrap"><img src={pass.qrImage} alt="Scannable ticket QR code"/><div><strong>Ready at the gate</strong><small>Brightness up. Keep this code private.</small></div></div></div>
    <a className="button secondary" href={`/api/v1/tickets/${id}/print`} target="_blank" rel="noreferrer">Download printable ticket</a></section>
}

function OrganizerPage() {
  const client = useQueryClient(); const [active, setActive] = useState<string>(); const events = useQuery({ queryKey: ['my-events'], queryFn: () => api<OrganizerEvent[]>('/api/v1/events/mine') })
  const create = useMutation({ mutationFn: (form: HTMLFormElement) => { const value = Object.fromEntries(new FormData(form)); return api<{id:string}>('/api/v1/events', { method: 'POST', body: JSON.stringify({ ...value, startsAt: new Date(String(value.startsAt)).toISOString(), endsAt: new Date(String(value.endsAt)).toISOString(), salesStart: new Date().toISOString(), salesEnd: new Date(String(value.startsAt)).toISOString() }) }) },
    onSuccess: data => { setActive(data.id); client.invalidateQueries({ queryKey: ['my-events'] }) } })
  const publish = useMutation({ mutationFn: async (id: string) => { const now = new Date(); const end = new Date(); end.setFullYear(end.getFullYear()+1); await api(`/api/v1/events/${id}/tiers`, { method:'POST', body: JSON.stringify({ name:'General admission', priceKobo:750000, capacity:100, salesStart:now.toISOString(), salesEnd:end.toISOString() }) }); return api(`/api/v1/events/${id}/publish`, { method:'POST' }) }, onSuccess: () => client.invalidateQueries({queryKey:['my-events']}) })
  const selected = active ?? events.data?.[0]?.id; const dashboard = useQuery({ queryKey: ['dashboard', selected], queryFn: () => api<{summary:{confirmedOrders:number;ticketsSold:number;grossKobo:number;pendingExceptions:number}}>(`/api/v1/events/${selected}/dashboard`), enabled: Boolean(selected) })
  return <section className="section page organizer"><div className="section-heading"><div><span className="kicker">Organizer studio</span><h1>Build a full house.</h1></div><p>Publish, sell, and keep the gate moving.</p></div>
    <div className="organizer-grid"><form className="panel form" onSubmit={e => {e.preventDefault(); create.mutate(e.currentTarget)}}><h2>New event</h2><label>Event title<input name="title" required/></label><label>Description<textarea name="description" required/></label><label>Location<input name="location" required/></label><label>Category<input name="category" defaultValue="Culture" required/></label><label>Starts<input name="startsAt" type="datetime-local" required/></label><label>Ends<input name="endsAt" type="datetime-local" required/></label><label>Timezone<input name="timeZone" defaultValue="Africa/Lagos" required/></label><button className="primary" disabled={create.isPending}>Save draft</button>{create.isError ? <p className="form-error">{create.error.message}</p> : null}</form>
      <div><div className="metric-grid"><Metric label="Gross sales" value={money(dashboard.data?.summary.grossKobo ?? 0)}/><Metric label="Tickets sold" value={String(dashboard.data?.summary.ticketsSold ?? 0)}/><Metric label="Exceptions" value={String(dashboard.data?.summary.pendingExceptions ?? 0)}/></div>
        <div className="panel"><h2>Your events</h2>{events.isPending ? <PageLoading/> : events.data?.length ? events.data.map(item => <button className="event-manage" key={item.id} onClick={() => setActive(item.id)}><span><strong>{item.title}</strong><small>{item.status} · {item.sold}/{item.capacity} sold</small></span>{item.status === 'Draft' ? <span className="button primary small" onClick={event => {event.stopPropagation(); publish.mutate(item.id)}}>Add tier & publish</span> : <span>View report</span>}</button>) : <Empty title="No events yet" body="Create your first draft."/>}</div></div></div></section>
}

function Metric({ label, value }: {label:string;value:string}) { return <div className="metric"><span>{label}</span><strong>{value}</strong></div> }

function ScannerPage() {
  const [eventId, setEventId] = useState(''); const [code, setCode] = useState(''); const [camera, setCamera] = useState(false); const video = useRef<HTMLVideoElement>(null)
  const scan = useMutation({ mutationFn: () => api<{result:string;message:string;checkedInAt?:string}>(`/api/v1/tickets/events/${eventId}/check-in`, {method:'POST',body:JSON.stringify({code})}) })
  const startCamera = async () => { try { const stream = await navigator.mediaDevices.getUserMedia({video:{facingMode:'environment'}}); if(video.current) video.current.srcObject=stream; setCamera(true) } catch { setCamera(false) } }
  return <section className="scanner-page"><div className="scanner-copy"><span className="kicker">Gate mode</span><h1>Fast in.<br/>Clear answers.</h1><p>Online validation checks the event, ticket status, and duplicate admissions in one atomic step.</p></div><div className="scanner panel"><div className="scanner-window">{camera ? <video ref={video} autoPlay playsInline aria-label="Camera preview"/> : <><QrCode/><p>Camera starts only when you ask.</p><button className="secondary" onClick={startCamera}>Enable camera</button></>}</div>
    <label>Event ID<input value={eventId} onChange={e=>setEventId(e.target.value)} placeholder="Assigned event UUID"/></label><label>Manual ticket code<textarea value={code} onChange={e=>setCode(e.target.value)} placeholder="gm1.…"/></label><button className="primary" disabled={!eventId||!code||scan.isPending} onClick={()=>scan.mutate()}>Validate ticket</button>
    {scan.data ? <div className={`scan-result ${scan.data.result}`} role="status"><CheckCircle2/><div><strong>{scan.data.result.replace('_',' ')}</strong><p>{scan.data.message}</p></div></div> : null}{scan.isError ? <p className="form-error">{scan.error.message}</p>:null}</div></section>
}

function SimulatorPage() {
  const [params] = useSearchParams(); const reference=params.get('reference') ?? ''; const navigate=useNavigate(); const complete=useMutation({mutationFn:()=>fetch(`/api/v1/orders/simulator/${reference}/complete`,{method:'POST'}).then(async r=>{if(!r.ok)throw new Error('Simulation is unavailable.');return r.json()}),onSuccess:()=>setTimeout(()=>navigate('/orders'),900)})
  return <section className="simulator"><div className="panel"><div className="test-badge">Local simulator · never a real charge</div><ShieldCheck className="sim-icon"/><h1>Complete test payment</h1><p>This development-only screen simulates a provider-confirmed test transaction. No card data is collected.</p><code>{reference}</code><button className="primary" disabled={complete.isPending||!reference} onClick={()=>complete.mutate()}>{complete.isPending?'Confirming…':'Approve simulated payment'}</button>{complete.data?<p className="success">Payment confirmed. Issuing tickets…</p>:null}{complete.isError?<p className="form-error">{complete.error.message}</p>:null}</div></section>
}

function PaymentReturn() {
  const [params]=useSearchParams(); const reference=params.get('reference'); const verify=useQuery({queryKey:['verify-payment',reference],queryFn:()=>api(`/api/v1/orders/payments/${reference}/verify`,{method:'POST'}),enabled:Boolean(reference),retry:false})
  return <section className="simulator"><div className="panel"><Clock3 className="sim-icon"/><h1>Checking payment</h1><p>{verify.isPending?'We are asking the provider for the latest status.':verify.isSuccess?'Payment confirmed. Your tickets are ready.':verify.isError?verify.error.message:'Missing payment reference.'}</p>{verify.isSuccess?<Link className="button primary" to="/orders">View my tickets</Link>:null}</div></section>
}

function CardsSkeleton(){return <div className="event-grid">{[1,2,3].map(value=><div className="event-card skeleton" key={value}/>)}</div>}
function PageLoading(){return <div className="page-loading" role="status"><span/> Loading GateMint…</div>}
function ErrorState({message}:{message:string}){return <div className="state error"><h2>Something went sideways</h2><p>{message}</p></div>}
function Empty({title,body}:{title:string;body:string}){return <div className="state"><TicketCheck/><h3>{title}</h3><p>{body}</p></div>}
function NotFound(){return <div className="state page"><h1>That door is closed.</h1><p>The page you requested does not exist.</p><Link className="button primary" to="/">Discover events</Link></div>}

export default App
