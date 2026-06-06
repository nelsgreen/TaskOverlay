//go:build windows

package main

import (
	"encoding/json"
	"fmt"
	"io"
	"math"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"runtime/debug"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

type HWND uintptr
type HINSTANCE uintptr
type HICON uintptr
type HCURSOR uintptr
type HBRUSH uintptr
type HDC uintptr
type HFONT uintptr
type HGDIOBJ uintptr

type RECT struct {
	Left   int32
	Top    int32
	Right  int32
	Bottom int32
}

type POINT struct {
	X int32
	Y int32
}

type MINMAXINFO struct {
	PtReserved     POINT
	PtMaxSize      POINT
	PtMaxPosition  POINT
	PtMinTrackSize POINT
	PtMaxTrackSize POINT
}

type MSG struct {
	Hwnd    HWND
	Message uint32
	WParam  uintptr
	LParam  uintptr
	Time    uint32
	Pt      POINT
}

type PAINTSTRUCT struct {
	Hdc         HDC
	FErase      int32
	RcPaint     RECT
	FRestore    int32
	FIncUpdate  int32
	RgbReserved [32]byte
}

type WNDCLASSEX struct {
	CbSize        uint32
	Style         uint32
	LpfnWndProc   uintptr
	CbClsExtra    int32
	CbWndExtra    int32
	HInstance     HINSTANCE
	HIcon         HICON
	HCursor       HCURSOR
	HbrBackground HBRUSH
	LpszMenuName  *uint16
	LpszClassName *uint16
	HIconSm       HICON
}

const (
	WS_OVERLAPPED    = 0x00000000
	WS_POPUP         = 0x80000000
	WS_THICKFRAME    = 0x00040000
	WS_CHILD         = 0x40000000
	WS_VISIBLE       = 0x10000000
	WS_BORDER        = 0x00800000
	WS_CLIPCHILDREN  = 0x02000000
	WS_EX_LAYERED    = 0x00080000
	WS_EX_TOOLWINDOW = 0x00000080
	WS_EX_TOPMOST  = 0x00000008
	ES_AUTOHSCROLL   = 0x0080

	LWA_ALPHA = 0x00000002

	WM_CREATE        = 0x0001
	WM_DESTROY       = 0x0002
	WM_ACTIVATE      = 0x0006
	WM_MOVE          = 0x0003
	WM_SIZE          = 0x0005
	WM_PAINT         = 0x000F
	WM_CLOSE         = 0x0010
	WM_NCHITTEST     = 0x0084
	WM_COMMAND       = 0x0111
	WM_TIMER         = 0x0113
	WM_LBUTTONDOWN   = 0x0201
	WM_LBUTTONUP     = 0x0202
	WM_MOUSEMOVE     = 0x0200
	WM_MOUSEWHEEL    = 0x020A
	WM_NCLBUTTONDOWN = 0x00A1
	WM_SETFOCUS      = 0x0007
	WM_KILLFOCUS     = 0x0008
	WM_KEYDOWN       = 0x0100
	WM_CHAR          = 0x0102
	WM_GETMINMAXINFO = 0x0024
	WM_ENTERSIZEMOVE = 0x0231
	WM_EXITSIZEMOVE  = 0x0232

	EN_KILLFOCUS = 0x0200

	VK_RETURN  = 0x0D
	VK_ESCAPE  = 0x1B
	VK_BACK    = 0x08
	VK_DELETE  = 0x2E
	VK_CONTROL = 0x11
	VK_V       = 0x56

	HTCLIENT      = 1
	HTCAPTION     = 2
	HTRIGHT       = 11
	HTBOTTOM      = 15
	HTBOTTOMRIGHT = 17

	DT_LEFT         = 0x00000000
	DT_CENTER       = 0x00000001
	DT_RIGHT        = 0x00000002
	DT_VCENTER      = 0x00000004
	DT_SINGLELINE   = 0x00000020
	DT_END_ELLIPSIS = 0x00008000
	DT_NOPREFIX     = 0x00000800
	DT_WORDBREAK    = 0x00000010

	TRANSPARENT = 1
	PS_SOLID    = 0
	NULL_BRUSH  = 5
	FW_NORMAL   = 400
	FW_BOLD     = 700

	TIMER_BLINK  = 1
	TIMER_STATUS = 2
	TIMER_SAVE   = 3

	WA_INACTIVE = 0

	ERROR_ALREADY_EXISTS = 183
	MB_OK                = 0
	MB_ICONWARNING       = 0x00000030
	CF_UNICODETEXT       = 13

	SWP_NOMOVE     = 0x0002
	SWP_NOSIZE     = 0x0001
	SWP_NOACTIVATE = 0x0010
)

var (
	user32   = syscall.NewLazyDLL("user32.dll")
	gdi32    = syscall.NewLazyDLL("gdi32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")

	procRegisterClassExW           = user32.NewProc("RegisterClassExW")
	procCreateWindowExW            = user32.NewProc("CreateWindowExW")
	procDefWindowProcW             = user32.NewProc("DefWindowProcW")
	procShowWindow                 = user32.NewProc("ShowWindow")
	procUpdateWindow               = user32.NewProc("UpdateWindow")
	procGetMessageW                = user32.NewProc("GetMessageW")
	procTranslateMessage           = user32.NewProc("TranslateMessage")
	procDispatchMessageW           = user32.NewProc("DispatchMessageW")
	procPostQuitMessage            = user32.NewProc("PostQuitMessage")
	procBeginPaint                 = user32.NewProc("BeginPaint")
	procEndPaint                   = user32.NewProc("EndPaint")
	procInvalidateRect             = user32.NewProc("InvalidateRect")
	procGetClientRect              = user32.NewProc("GetClientRect")
	procSetLayeredWindowAttributes = user32.NewProc("SetLayeredWindowAttributes")
	procReleaseCapture             = user32.NewProc("ReleaseCapture")
	procSendMessageW               = user32.NewProc("SendMessageW")
	procDestroyWindow              = user32.NewProc("DestroyWindow")
	procSetTimer                   = user32.NewProc("SetTimer")
	procKillTimer                  = user32.NewProc("KillTimer")
	procSetFocus                   = user32.NewProc("SetFocus")
	procSetWindowTextW             = user32.NewProc("SetWindowTextW")
	procGetWindowTextLengthW       = user32.NewProc("GetWindowTextLengthW")
	procGetWindowTextW             = user32.NewProc("GetWindowTextW")
	procMoveWindow                 = user32.NewProc("MoveWindow")
	procSetWindowPos               = user32.NewProc("SetWindowPos")
	procGetWindowRect              = user32.NewProc("GetWindowRect")
	procLoadCursorW                = user32.NewProc("LoadCursorW")
	procSetCapture                 = user32.NewProc("SetCapture")
	procGetCursorPos               = user32.NewProc("GetCursorPos")
	procOpenClipboard              = user32.NewProc("OpenClipboard")
	procCloseClipboard             = user32.NewProc("CloseClipboard")
	procGetClipboardData           = user32.NewProc("GetClipboardData")
	procIsClipboardFormatAvailable = user32.NewProc("IsClipboardFormatAvailable")
	procGetKeyState                = user32.NewProc("GetKeyState")

	procCreateSolidBrush = gdi32.NewProc("CreateSolidBrush")
	procDeleteObject     = gdi32.NewProc("DeleteObject")
	procFillRect         = user32.NewProc("FillRect")
	procSetBkMode        = gdi32.NewProc("SetBkMode")
	procSetTextColor     = gdi32.NewProc("SetTextColor")
	procDrawTextW        = user32.NewProc("DrawTextW")
	procCreateFontW      = gdi32.NewProc("CreateFontW")
	procSelectObject     = gdi32.NewProc("SelectObject")
	procCreatePen        = gdi32.NewProc("CreatePen")
	procRectangle        = gdi32.NewProc("Rectangle")
	procMoveToEx         = gdi32.NewProc("MoveToEx")
	procLineTo           = gdi32.NewProc("LineTo")
	procGetStockObject   = gdi32.NewProc("GetStockObject")

	procGetModuleHandleW = kernel32.NewProc("GetModuleHandleW")
	procCreateMutexW     = kernel32.NewProc("CreateMutexW")
	procGetLastError     = kernel32.NewProc("GetLastError")
	procReplaceFileW     = kernel32.NewProc("ReplaceFileW")
	procGlobalLock       = kernel32.NewProc("GlobalLock")
	procGlobalUnlock     = kernel32.NewProc("GlobalUnlock")
	procMessageBoxW      = user32.NewProc("MessageBoxW")
)

type ColorOption struct {
	Name  string
	Color uint32
}

func bgOptions() []ColorOption {
	return []ColorOption{
		{Name: "темный графит", Color: rgb(22, 25, 30)},
		{Name: "мягкий серый", Color: rgb(54, 57, 63)},
		{Name: "теплый серый", Color: rgb(70, 66, 58)},
		{Name: "сине-серый", Color: rgb(28, 39, 52)},
		{Name: "зеленый темный", Color: rgb(25, 55, 48)},
		{Name: "светлый", Color: rgb(235, 235, 235)},
	}
}

func textOptions() []ColorOption {
	return []ColorOption{
		{Name: "теплый белый", Color: rgb(245, 245, 235)},
		{Name: "белый", Color: rgb(250, 250, 250)},
		{Name: "спокойный желтый", Color: rgb(255, 232, 120)},
		{Name: "янтарный", Color: rgb(255, 198, 88)},
		{Name: "голубой", Color: rgb(150, 225, 255)},
		{Name: "мята", Color: rgb(170, 245, 205)},
		{Name: "лаванда", Color: rgb(210, 190, 255)},
		{Name: "коралловый", Color: rgb(255, 150, 130)},
		{Name: "черный", Color: rgb(20, 20, 20)},
		{Name: "синий", Color: rgb(110, 170, 255)},
	}
}

func colorName(opts []ColorOption, color uint32) string {
	for _, opt := range opts {
		if opt.Color == color {
			return opt.Name
		}
	}
	return "свой цвет"
}

type Task struct {
	ID          int64         `json:"id"`
	ParentID    int64         `json:"parent_id"`
	Text        string        `json:"text"`
	Description string        `json:"description,omitempty"`
	Done        bool          `json:"done"`
	Priority    int           `json:"priority"`
	InWork      bool          `json:"in_work"`
	DueHHMM     string        `json:"due_hhmm"`
	Ack         bool          `json:"ack"`
	Blink       bool          `json:"blink"`
	Expanded    bool          `json:"expanded"`
	CreatedAt   time.Time     `json:"created_at"`
	DoneAt      *time.Time    `json:"done_at,omitempty"`
}

type Settings struct {
	X                 int32  `json:"x"`
	Y                 int32  `json:"y"`
	W                 int32  `json:"w"`
	H                 int32  `json:"h"`
	BgColor           uint32 `json:"bg_color"`
	TextColor         uint32 `json:"text_color"`
	BorderColor       uint32 `json:"border_color"`
	Alpha             byte   `json:"alpha"`
	BgAlpha           byte   `json:"bg_alpha"`
	TextAlpha         byte   `json:"text_alpha"`
	FontSize          int32  `json:"font_size"`
	Bold              bool   `json:"bold"`
	Shadow            bool   `json:"shadow"`
	Outline           bool   `json:"outline"`
	ShowBorders       bool   `json:"show_borders"`
	DoneStyle         int    `json:"done_style"`
	CollapseDone      bool   `json:"collapse_done"`
	CompletedExpanded bool   `json:"completed_expanded"`
}

type State struct {
	SchemaVersion int      `json:"schema_version"`
	AppVersion    string   `json:"app_version"`
	NextID        int64    `json:"next_id"`
	Tasks         []Task   `json:"tasks"`
	Settings      Settings `json:"settings"`
}

type Action struct {
	Rect   RECT
	Kind   string
	TaskID int64
	Value  int
}

type App struct {
	hwnd                HWND
	state               State
	actions             []Action
	settingsOpen        bool
	settingsPaintLogged bool
	scroll              int32
	blinkOn             bool
	editActive          bool
	editTaskID          int64
	editKind            string
	editText            string
	editOriginal        string
	editReplaceOnType   bool
	editCreatedNew      bool
	detailsTaskID       int64
	dropdown            string
	windowActive        bool
	saveScheduled       bool
	lastSaveReason      string
	sizing              bool
	status              string
	statusUntil         time.Time
}

const (
	AppVersion    = "13.0.0"
	SchemaVersion = 13
	BaseW         = int32(620)
	BaseH         = int32(520)
)

var app *App
var wndProcPtr uintptr

var (
	logMu            sync.Mutex
	logFilePath      string
	sessionStartedAt time.Time
	instanceMutex    uintptr
	crashFile        *os.File
)

const DebugInput = false

func main() {
	runtime.LockOSThread()
	debug.SetTraceback("all")
	initLogger()
	initCrashOutput()
	previousClean, canStart := checkPreviousSession()
	if !canStart {
		return
	}
	if !acquireSingleInstance() {
		return
	}
	markSessionStart()
	logf("start version=%s go=%s os=%s arch=%s", AppVersion, runtime.Version(), runtime.GOOS, runtime.GOARCH)
	if exe, err := os.Executable(); err == nil {
		logf("exe=%s", exe)
	}
	logf("state=%s", statePath())
	logf("log=%s", logFilePath)
	defer func() {
		if r := recover(); r != nil {
			logf("panic in main: %v", r)
			logf("stack:\n%s", string(debug.Stack()))
			markSessionEnd(false)
			os.Exit(2)
		}
	}()

	createShortcutSafe()
	st := loadState()
	app = &App{state: st}
	if !previousClean {
		app.status = "Прошлый запуск завершился некорректно. Выгрузите диагностику в настройках."
		app.statusUntil = time.Now().Add(8 * time.Second)
	}
	run()
	markSessionEnd(true)
	logf("stop clean")
}

func run() {
	hInst, _, _ := procGetModuleHandleW.Call(0)
	className := utf16Ptr("TaskOverlayV13Window")
	cursor, _, _ := procLoadCursorW.Call(0, uintptr(32512))

	wndProcPtr = syscall.NewCallback(wndProc)
	wc := WNDCLASSEX{
		CbSize:        uint32(unsafe.Sizeof(WNDCLASSEX{})),
		Style:         0,
		LpfnWndProc:   wndProcPtr,
		HInstance:     HINSTANCE(hInst),
		HCursor:       HCURSOR(cursor),
		HbrBackground: 0,
		LpszClassName: className,
	}
	if r, _, err := procRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc))); r == 0 {
		logf("RegisterClassExW returned 0 err=%v", err)
	}

	s := app.state.Settings
	hwnd, _, _ := procCreateWindowExW.Call(
		uintptr(WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_TOPMOST),
		uintptr(unsafe.Pointer(className)),
		uintptr(unsafe.Pointer(utf16Ptr("TaskOverlay"))),
		uintptr(WS_POPUP|WS_THICKFRAME|WS_VISIBLE|WS_CLIPCHILDREN),
		uintptr(s.X), uintptr(s.Y), uintptr(s.W), uintptr(s.H),
		0, 0, hInst, 0,
	)
	if hwnd == 0 {
		logf("CreateWindowExW failed")
		return
	}
	app.hwnd = HWND(hwnd)
	logf("window created hwnd=%d x=%d y=%d w=%d h=%d", hwnd, s.X, s.Y, s.W, s.H)
	app.windowActive = false
	applyTopMost()
	applyAlpha()
	procShowWindow.Call(hwnd, 1)
	procUpdateWindow.Call(hwnd)
	procSetTimer.Call(hwnd, TIMER_BLINK, 500, 0)

	var msg MSG
	for {
		r, _, err := procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(r) == -1 {
			logf("GetMessageW error err=%v", err)
			break
		}
		if int32(r) == 0 {
			break
		}
		procTranslateMessage.Call(uintptr(unsafe.Pointer(&msg)))
		procDispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
	}
}

func wndProc(hwnd HWND, msg uint32, wParam, lParam uintptr) (ret uintptr) {
	defer func() {
		if r := recover(); r != nil {
			logf("panic in wndProc msg=0x%X wParam=%d lParam=%d: %v", msg, wParam, lParam, r)
			logf("stack:\n%s", string(debug.Stack()))
			if app != nil {
				app.status = "Ошибка записана в лог. Выгрузите диагностику в настройках."
				app.statusUntil = time.Now().Add(8 * time.Second)
			}
			ret = 0
		}
	}()
	switch msg {
	case WM_CREATE:
		return 0
	case WM_ACTIVATE:
		active := (wParam & 0xffff) != WA_INACTIVE
		if app != nil {
			app.windowActive = active
			applyAlpha()
			invalidate()
		}
		return 0
	case WM_SETFOCUS:
		if app != nil { app.windowActive = true; applyAlpha(); invalidate() }
		return 0
	case WM_KILLFOCUS:
		if app != nil { app.windowActive = false; applyAlpha(); invalidate() }
		return 0
	case WM_PAINT:
		app.paint()
		return 0
	case WM_TIMER:
		if wParam == TIMER_BLINK {
			app.blinkOn = !app.blinkOn
			applyTopMost()
			app.checkDue()
			invalidate()
		} else if wParam == TIMER_STATUS {
			if time.Now().After(app.statusUntil) {
				app.status = ""
				procKillTimer.Call(uintptr(hwnd), TIMER_STATUS)
				invalidate()
			}
		} else if wParam == TIMER_SAVE {
			procKillTimer.Call(uintptr(hwnd), TIMER_SAVE)
			app.saveScheduled = false
			logf("save timer begin reason=%s", app.lastSaveReason)
			saveState(app.state)
			logf("save timer end reason=%s", app.lastSaveReason)
		}
		return 0
	case WM_COMMAND:
		return 0
	case WM_GETMINMAXINFO:
		if lParam != 0 {
			mmi := (*MINMAXINFO)(unsafe.Pointer(lParam))
			mmi.PtMinTrackSize.X = 420
			mmi.PtMinTrackSize.Y = 320
			mmi.PtMaxTrackSize.X = 1800
			mmi.PtMaxTrackSize.Y = 1400
		}
		return 0
	case WM_KEYDOWN:
		if app.handleKeyDown(wParam) {
			return 0
		}
	case WM_CHAR:
		if app.handleChar(wParam) {
			return 0
		}
	case WM_LBUTTONDOWN:
		x, y := getXY(lParam)
		app.handleClick(x, y)
		return 0
	case WM_NCHITTEST:
		return app.hitTestResize(lParam)
	case WM_ENTERSIZEMOVE:
		app.sizing = true
		logf("sizing begin")
		return 0
	case WM_EXITSIZEMOVE:
		app.sizing = false
		app.captureWindowRect()
		app.scheduleSave("exit_size_move")
		logf("sizing end x=%d y=%d w=%d h=%d scale=%.2f", app.state.Settings.X, app.state.Settings.Y, app.state.Settings.W, app.state.Settings.H, app.uiScale())
		invalidate()
		return 0
	case WM_MOUSEWHEEL:
		delta := int16((wParam >> 16) & 0xffff)
		step := app.scale(28)
		if step < 18 {
			step = 18
		}
		if delta > 0 {
			app.scroll -= step
		} else {
			app.scroll += step
		}
		if app.scroll < 0 {
			app.scroll = 0
		}
		invalidate()
		return 0
	case WM_MOVE:
		app.captureWindowRect()
		if !app.sizing {
			app.scheduleSave("move")
		}
		return 0
	case WM_SIZE:
		app.captureWindowRect()
		invalidate()
		if !app.sizing {
			app.scheduleSave("size")
		}
		return 0
	case WM_CLOSE:
		logf("window close requested")
		app.finishEdit(true)
		saveState(app.state)
		procDestroyWindow.Call(uintptr(hwnd))
		return 0
	case WM_DESTROY:
		saveState(app.state)
		markSessionEnd(true)
		logf("window destroyed clean")
		procPostQuitMessage.Call(0)
		return 0
	}
	r, _, _ := procDefWindowProcW.Call(uintptr(hwnd), uintptr(msg), wParam, lParam)
	return r
}

func (a *App) paint() {
	var ps PAINTSTRUCT
	hdc, _, _ := procBeginPaint.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&ps)))
	var rc RECT
	procGetClientRect.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&rc)))

	fillRect(HDC(hdc), rc, a.state.Settings.BgColor)
	a.actions = nil

	a.drawHeader(HDC(hdc), rc)
	top := a.scale(38)
	if a.settingsOpen {
		if !a.settingsPaintLogged {
			logf("paint settings begin rc=%d,%d,%d,%d", rc.Left, rc.Top, rc.Right, rc.Bottom)
		}
		top = a.drawSettings(HDC(hdc), rc, top)
		if !a.settingsPaintLogged {
			logf("paint settings end top=%d", top)
			a.settingsPaintLogged = true
		}
	}
	a.drawTasks(HDC(hdc), rc, top)
	if a.status != "" {
		r := RECT{Left: a.scale(12), Top: rc.Bottom - a.scale(26), Right: rc.Right - a.scale(12), Bottom: rc.Bottom - a.scale(4)}
		drawText(HDC(hdc), a.status, r, a.effectiveTextColor(), a.font(max(14, a.state.Settings.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX)
	}
	a.drawResizeGrip(HDC(hdc), rc)
	procEndPaint.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&ps)))
}

func (a *App) drawHeader(hdc HDC, rc RECT) {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	m := a.scale(12)
	title := RECT{Left: m, Top: a.scale(7), Right: rc.Right - a.scale(110), Bottom: a.scale(32)}
	drawText(hdc, "TaskOverlay", title, tc, a.font(max(15, s.FontSize-4)), true, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	btnW := a.scale(26)
	gap := a.scale(6)
	btnTop := a.scale(5)
	btnBot := a.scale(31)
	closeR := RECT{Left: rc.Right - m - btnW, Top: btnTop, Right: rc.Right - m, Bottom: btnBot}
	addR := RECT{Left: closeR.Left - gap - btnW, Top: btnTop, Right: closeR.Left - gap, Bottom: btnBot}
	setR := RECT{Left: addR.Left - gap - btnW, Top: btnTop, Right: addR.Left - gap, Bottom: btnBot}
	a.addAction(setR, "settings", 0, 0)
	a.addAction(addR, "add", 0, 0)
	a.addAction(closeR, "close", 0, 0)
	drawButton(hdc, "⚙", setR, tc, a.font(s.FontSize))
	drawButton(hdc, "+", addR, tc, a.font(s.FontSize+2))
	drawButton(hdc, "x", closeR, tc, a.font(s.FontSize))
}

func (a *App) drawSettings(hdc HDC, rc RECT, top int32) int32 {
	s := &a.state.Settings
	tc := a.effectiveTextColor()
	panelH := a.scale(245)
	if a.dropdown == "bg" {
		panelH += a.scale(int32(len(bgOptions())) * 24)
	}
	if a.dropdown == "text" {
		panelH += a.scale(int32(len(textOptions())) * 24)
	}
	panel := RECT{Left: a.scale(8), Top: top, Right: rc.Right - a.scale(8), Bottom: top + panelH}
	if s.ShowBorders {
		drawBorder(hdc, panel, s.BorderColor)
	}
	y := top + a.scale(8)
	drawTextSafe(hdc, "Настройки", RECT{Left: a.scale(16), Top: y, Right: rc.Right - a.scale(16), Bottom: y + a.scale(22)}, tc, a.font(s.FontSize), true, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	y += a.scale(28)
	y = a.dropdownLine(hdc, y, "Фон", colorName(bgOptions(), s.BgColor), "drop_bg")
	if a.dropdown == "bg" {
		y = a.drawColorOptions(hdc, y, "bg", bgOptions(), s.BgColor)
	}
	y = a.dropdownLine(hdc, y, "Текст", colorName(textOptions(), s.TextColor), "drop_text")
	if a.dropdown == "text" {
		y = a.drawColorOptions(hdc, y, "text", textOptions(), s.TextColor)
	}
	y += a.scale(2)
	a.settingsLineSafe(hdc, y, "Фон прозрачн.", []string{"-", strconv.Itoa(int(s.BgAlpha)), "+"}, []string{"bg_alpha_minus", "noop", "bg_alpha_plus"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Текст прозрачн.", []string{"-", strconv.Itoa(int(s.TextAlpha)), "+"}, []string{"text_alpha_minus", "noop", "text_alpha_plus"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Шрифт", []string{"-", strconv.Itoa(int(s.FontSize)), "+", "B", "тень", "объем"}, []string{"font_minus", "noop", "font_plus", "toggle_bold", "toggle_shadow", "toggle_outline"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Выполненные", []string{"развернуть", "стиль", "рамка"}, []string{"toggle_completed", "toggle_done_style", "toggle_border"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Экспорт", []string{"сегодня", "7 дней", "30 дней", "все"}, []string{"export_1", "export_7", "export_30", "export_all"})
	y += a.scale(26)
	a.settingsLineSafe(hdc, y, "Диагностика", []string{"выгрузить", "логи", "сброс окна"}, []string{"diag_export", "open_logs", "reset_window"})
	return panel.Bottom + a.scale(8)
}

func (a *App) dropdownLine(hdc HDC, y int32, label, value, kind string) int32 {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	drawTextSafe(hdc, label+":", RECT{Left: a.scale(18), Top: y, Right: a.scale(135), Bottom: y + a.scale(22)}, tc, a.font(max(14, s.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	r := RECT{Left: a.scale(140), Top: y, Right: a.scale(300), Bottom: y + a.scale(22)}
	a.addAction(r, kind, 0, 0)
	drawBorder(hdc, r, s.BorderColor)
	drawButtonSafe(hdc, value+" ▼", r, tc, a.font(max(14, s.FontSize-3)))
	return y + a.scale(26)
}

func (a *App) drawColorOptions(hdc HDC, y int32, group string, opts []ColorOption, selected uint32) int32 {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	for i, opt := range opts {
		r := RECT{Left: a.scale(140), Top: y, Right: a.scale(330), Bottom: y + a.scale(22)}
		kind := "set_" + group + "_" + strconv.Itoa(i)
		a.addAction(r, kind, 0, 0)
		fillRect(hdc, RECT{Left: r.Left + a.scale(4), Top: r.Top + a.scale(4), Right: r.Left + a.scale(20), Bottom: r.Bottom - a.scale(4)}, opt.Color)
		if opt.Color == selected {
			drawBorder(hdc, r, tc)
		}
		drawTextSafe(hdc, opt.Name, RECT{Left: r.Left + a.scale(26), Top: r.Top, Right: r.Right - a.scale(4), Bottom: r.Bottom}, tc, a.font(max(14, s.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
		y += a.scale(24)
	}
	return y + a.scale(2)
}

func (a *App) settingsLine(hdc HDC, y int32, label string, texts []string, kinds []string) {
	s := a.state.Settings
	drawText(hdc, label+":", RECT{Left: 18, Top: y, Right: 135, Bottom: y + 22}, s.TextColor, max(14, s.FontSize-3), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	x := int32(140)
	for i, t := range texts {
		w := int32(20 + len([]rune(t))*9)
		if w < 32 {
			w = 32
		}
		r := RECT{Left: x, Top: y, Right: x + w, Bottom: y + 22}
		a.addAction(r, kinds[i], 0, 0)
		drawButton(hdc, t, r, s.TextColor, max(14, s.FontSize-3))
		x += w + 8
	}
}

func (a *App) settingsLineSafe(hdc HDC, y int32, label string, texts []string, kinds []string) {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	drawTextSafe(hdc, label+":", RECT{Left: a.scale(18), Top: y, Right: a.scale(135), Bottom: y + a.scale(22)}, tc, a.font(max(14, s.FontSize-3)), false, false, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	x := a.scale(140)
	for i, t := range texts {
		if i >= len(kinds) {
			break
		}
		w := a.scale(22 + int32(len([]rune(t))*9))
		if w < a.scale(32) {
			w = a.scale(32)
		}
		if x+w > 3000 {
			break
		}
		r := RECT{Left: x, Top: y, Right: x + w, Bottom: y + a.scale(22)}
		a.addAction(r, kinds[i], 0, 0)
		drawButtonSafe(hdc, t, r, tc, a.font(max(14, s.FontSize-3)))
		x += w + a.scale(8)
	}
}

func (a *App) drawTasks(hdc HDC, rc RECT, top int32) {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	y := top - a.scroll
	drawRow := func(task Task, y int32) int32 {
		indent := int32(0)
		if task.ParentID != 0 {
			indent = a.scale(24)
		}
		rowH := a.scale(max(30, s.FontSize+12))
		if y+rowH < top {
			return y + rowH + a.scale(6)
		}
		if y > rc.Bottom-a.scale(32) {
			return y + rowH + a.scale(6)
		}

		row := RECT{Left: a.scale(8) + indent, Top: y, Right: rc.Right - a.scale(8), Bottom: y + rowH}
		if task.Blink && a.blinkOn {
			fillRect(hdc, row, adjustColor(s.BgColor, 55))
		}
		if task.InWork {
			drawBorder(hdc, row, tc)
		} else if s.ShowBorders {
			drawBorder(hdc, row, s.BorderColor)
		}
		x := row.Left + a.scale(6)
		cb := RECT{Left: x, Top: y + a.scale(7), Right: x + a.scale(16), Bottom: y + a.scale(23)}
		a.addAction(cb, "toggle_done", task.ID, 0)
		drawBorder(hdc, cb, tc)
		if task.Done {
			drawText(hdc, "✓", RECT{Left: cb.Left - a.scale(1), Top: cb.Top - a.scale(3), Right: cb.Right + a.scale(5), Bottom: cb.Bottom + a.scale(3)}, tc, a.font(s.FontSize), true, false, false, DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
		}
		x += a.scale(22)

		if a.hasChildren(task.ID) {
			ex := RECT{Left: x, Top: y + a.scale(5), Right: x + a.scale(18), Bottom: y + a.scale(25)}
			a.addAction(ex, "expand", task.ID, 0)
			sym := "▸"
			if task.Expanded {
				sym = "▾"
			}
			drawButton(hdc, sym, ex, tc, a.font(s.FontSize))
		}
		x += a.scale(20)

		pr := priorityLabel(task.Priority)
		prR := RECT{Left: x, Top: y + a.scale(5), Right: x + a.scale(34), Bottom: y + a.scale(25)}
		a.addAction(prR, "priority", task.ID, 0)
		drawButton(hdc, pr, prR, tc, a.font(max(13, s.FontSize-4)))
		x += a.scale(38)

		textRight := rc.Right - a.scale(170)
		txt := RECT{Left: x, Top: y + a.scale(4), Right: textRight, Bottom: y + rowH - a.scale(4)}
		a.addAction(txt, "edit_text", task.ID, 0)
		color := tc
		if task.Done && s.DoneStyle == 1 {
			color = s.BorderColor
		}
		textToDraw := task.Text
		if a.editActive && a.editTaskID == task.ID && a.editKind == "text" {
			textToDraw = a.editText
			if a.blinkOn {
				textToDraw += "|"
			}
			drawBorder(hdc, txt, color)
		}
		drawText(hdc, textToDraw, txt, color, a.font(s.FontSize), s.Bold, s.Shadow, s.Outline, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX)
		if task.Done {
			drawStrike(hdc, txt, color)
		}

		bx := rc.Right - a.scale(162)
		buttons := []struct {
			txt, kind string
			w         int32
		}{
			{"+", "add_child", 22},
			{"i", "details", 22},
			{"⏰", "edit_due", 34},
			{"▶", "inwork", 24},
			{"×", "delete", 24},
		}
		for _, b := range buttons {
			bw := a.scale(b.w)
			r := RECT{Left: bx, Top: y + a.scale(5), Right: bx + bw, Bottom: y + a.scale(25)}
			a.addAction(r, b.kind, task.ID, 0)
			drawButton(hdc, b.txt, r, tc, a.font(max(14, s.FontSize-3)))
			bx += bw + a.scale(4)
		}
		due := task.DueHHMM
		if a.editActive && a.editTaskID == task.ID && a.editKind == "due" {
			due = a.editText
			if a.blinkOn {
				due += "|"
			}
		}
		if due == "" {
			due = "--:--"
		}
		dueR := RECT{Left: rc.Right - a.scale(70), Top: y + a.scale(5), Right: rc.Right - a.scale(10), Bottom: y + a.scale(25)}
		a.addAction(dueR, "edit_due", task.ID, 0)
		if a.editActive && a.editTaskID == task.ID && a.editKind == "due" {
			drawBorder(hdc, dueR, tc)
		}
		drawButton(hdc, due, dueR, tc, a.font(max(13, s.FontSize-4)))
		nextY := y + rowH + a.scale(6)
		if a.detailsTaskID == task.ID {
			detailsH := a.drawTaskDetails(hdc, rc, nextY, task)
			nextY += detailsH + a.scale(6)
		}
		return nextY
	}

	activeTasks := a.visibleTasks(false)
	for _, task := range activeTasks {
		if y > rc.Bottom-a.scale(32) {
			break
		}
		y = drawRow(task, y)
	}

	doneTasks := a.visibleTasks(true)
	if len(doneTasks) > 0 {
		headerH := a.scale(28)
		if y+headerH >= top && y <= rc.Bottom-a.scale(32) {
			head := RECT{Left: a.scale(8), Top: y, Right: rc.Right - a.scale(8), Bottom: y + headerH}
			a.addAction(head, "toggle_completed", 0, 0)
			label := "Завершенные: " + strconv.Itoa(len(doneTasks))
			if s.CompletedExpanded {
				label += "  ▾"
			} else {
				label += "  ▸"
			}
			drawText(hdc, label, head, tc, a.font(max(14, s.FontSize-4)), true, s.Shadow, false, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
		}
		y += headerH + a.scale(4)
		if s.CompletedExpanded {
			for _, task := range doneTasks {
				if y > rc.Bottom-a.scale(32) {
					break
				}
				y = drawRow(task, y)
			}
		}
	}
}

func (a *App) visibleTasks(done bool) []Task {
	var result []Task
	parents := make([]Task, 0)
	for _, t := range a.state.Tasks {
		if t.ParentID == 0 && t.Done == done {
			parents = append(parents, t)
		}
	}
	for _, p := range parents {
		result = append(result, p)
		if p.Expanded {
			for _, c := range a.state.Tasks {
				if c.ParentID == p.ID && c.Done == done {
					result = append(result, c)
				}
			}
		}
	}
	for _, c := range a.state.Tasks {
		if c.ParentID != 0 && c.Done == done && !a.hasParentWithDoneState(c.ParentID, done) {
			result = append(result, c)
		}
	}
	return result
}

func (a *App) hasParentWithDoneState(parentID int64, done bool) bool {
	for _, t := range a.state.Tasks {
		if t.ID == parentID {
			return t.Done == done
		}
	}
	return false
}

func (a *App) handleClick(x, y int32) {
	a.windowActive = true
	applyAlpha()
	a.finishEdit(true)
	for i := len(a.actions) - 1; i >= 0; i-- {
		act := a.actions[i]
		if inRect(x, y, act.Rect) {
			a.perform(act)
			return
		}
	}
	if y < a.scale(36) {
		procReleaseCapture.Call()
		procSendMessageW.Call(uintptr(a.hwnd), WM_NCLBUTTONDOWN, HTCAPTION, 0)
		a.captureWindowRect()
		saveState(a.state)
	}
}

func (a *App) perform(act Action) {
	defer func() {
		if r := recover(); r != nil {
			logf("panic in action kind=%s task=%d value=%d: %v", act.Kind, act.TaskID, act.Value, r)
			logf("stack:\n%s", string(debug.Stack()))
			a.setStatus("Ошибка действия записана в лог")
			invalidate()
		}
	}()
	logf("action kind=%s task=%d value=%d", act.Kind, act.TaskID, act.Value)
	if a.performDynamicAction(act.Kind) {
		a.scheduleSave(act.Kind)
		invalidate()
		logf("action invalidate done kind=%s", act.Kind)
		return
	}
	switch act.Kind {
	case "close":
		procSendMessageW.Call(uintptr(a.hwnd), WM_CLOSE, 0, 0)
	case "settings":
		logf("settings toggle begin open=%v", a.settingsOpen)
		a.settingsOpen = !a.settingsOpen
		a.settingsPaintLogged = false
		logf("settings toggle end open=%v", a.settingsOpen)
		invalidate()
		return
	case "add":
		a.addTask(0)
	case "toggle_done":
		a.updateTask(act.TaskID, func(t *Task) {
			t.Done = !t.Done
			if t.Done {
				now := time.Now()
				t.DoneAt = &now
				t.Blink = false
				t.Ack = true
			} else {
				t.DoneAt = nil
				t.Ack = false
			}
		})
	case "expand":
		a.updateTask(act.TaskID, func(t *Task) { t.Expanded = !t.Expanded })
	case "priority":
		a.updateTask(act.TaskID, func(t *Task) { t.Priority = (t.Priority + 1) % 4 })
	case "add_child":
		a.addTask(act.TaskID)
		a.updateTask(act.TaskID, func(t *Task) { t.Expanded = true })
	case "details":
		if a.detailsTaskID == act.TaskID {
			a.detailsTaskID = 0
		} else {
			a.detailsTaskID = act.TaskID
		}
	case "inwork":
		for i := range a.state.Tasks {
			a.state.Tasks[i].InWork = false
		}
		a.updateTask(act.TaskID, func(t *Task) { t.InWork = true })
	case "delete":
		a.deleteTask(act.TaskID)
	case "edit_text":
		a.startEdit(act.TaskID, "text", act.Rect)
	case "edit_due":
		a.startEdit(act.TaskID, "due", act.Rect)
	case "edit_description":
		a.startEdit(act.TaskID, "description", act.Rect)
	case "drop_bg":
		if a.dropdown == "bg" { a.dropdown = "" } else { a.dropdown = "bg" }
	case "drop_text":
		if a.dropdown == "text" { a.dropdown = "" } else { a.dropdown = "text" }
	case "bg_alpha_minus":
		if a.state.Settings.BgAlpha > 35 {
			a.state.Settings.BgAlpha -= 15
			a.state.Settings.Alpha = a.state.Settings.BgAlpha
			applyAlpha()
		}
	case "bg_alpha_plus":
		if a.state.Settings.BgAlpha < 255 {
			a.state.Settings.BgAlpha = byte(minInt(255, int(a.state.Settings.BgAlpha)+15))
			a.state.Settings.Alpha = a.state.Settings.BgAlpha
			applyAlpha()
		}
	case "text_alpha_minus":
		if a.state.Settings.TextAlpha > 35 {
			a.state.Settings.TextAlpha -= 15
		}
	case "text_alpha_plus":
		if a.state.Settings.TextAlpha < 255 {
			a.state.Settings.TextAlpha = byte(minInt(255, int(a.state.Settings.TextAlpha)+15))
		}
	case "font_minus":
		if a.state.Settings.FontSize > 12 {
			a.state.Settings.FontSize -= 1
		}
	case "font_plus":
		if a.state.Settings.FontSize < 36 {
			a.state.Settings.FontSize += 1
		}
	case "toggle_bold":
		a.state.Settings.Bold = !a.state.Settings.Bold
	case "toggle_shadow":
		a.state.Settings.Shadow = !a.state.Settings.Shadow
	case "toggle_outline":
		a.state.Settings.Outline = !a.state.Settings.Outline
	case "toggle_border":
		a.state.Settings.ShowBorders = !a.state.Settings.ShowBorders
	case "toggle_completed":
		a.state.Settings.CompletedExpanded = !a.state.Settings.CompletedExpanded
	case "toggle_collapse_done":
		a.state.Settings.CompletedExpanded = !a.state.Settings.CompletedExpanded
	case "toggle_done_style":
		a.state.Settings.DoneStyle = (a.state.Settings.DoneStyle + 1) % 2
	case "export_1":
		a.export(1)
	case "export_7":
		a.export(7)
	case "export_30":
		a.export(30)
	case "export_all":
		a.export(0)
	case "diag_export":
		logf("diag_export action begin")
		a.exportDiagnostics()
		logf("diag_export action end")
		invalidate()
		return
	case "open_logs":
		a.openLogFolder()
	case "reset_window":
		a.resetWindow()
	}
	a.scheduleSave(act.Kind)
	invalidate()
	logf("action invalidate done kind=%s", act.Kind)
}

func (a *App) performDynamicAction(kind string) bool {
	if strings.HasPrefix(kind, "set_bg_") {
		idx, err := strconv.Atoi(strings.TrimPrefix(kind, "set_bg_"))
		opts := bgOptions()
		if err == nil && idx >= 0 && idx < len(opts) {
			a.state.Settings.BgColor = opts[idx].Color
			a.dropdown = ""
			return true
		}
	}
	if strings.HasPrefix(kind, "set_text_") {
		idx, err := strconv.Atoi(strings.TrimPrefix(kind, "set_text_"))
		opts := textOptions()
		if err == nil && idx >= 0 && idx < len(opts) {
			a.state.Settings.TextColor = opts[idx].Color
			a.dropdown = ""
			return true
		}
	}
	return false
}

func (a *App) addTask(parent int64) {
	if a.state.NextID < 1 {
		a.state.NextID = 1
	}
	title := "Новая задача"
	if parent != 0 {
		title = "Новая подзадача"
	}
	description := ""
	if clip, err := clipboardText(); err == nil && clip != "" {
		ct, cd := deriveTitleAndDescription(clip)
		if ct != "" {
			title = ct
			description = cd
		}
		logf("clipboard prefill used=true chars=%d title_chars=%d description_chars=%d", len([]rune(clip)), len([]rune(title)), len([]rune(description)))
	} else if err != nil {
		logf("clipboard prefill used=false err=%v", err)
	}
	id := a.state.NextID
	a.state.Tasks = append(a.state.Tasks, Task{ID: id, ParentID: parent, Text: title, Description: description, CreatedAt: time.Now(), Expanded: true})
	a.state.NextID++
	a.detailsTaskID = id
	a.startEdit(id, "text", RECT{})
	a.editCreatedNew = true
}

func (a *App) updateTask(id int64, fn func(*Task)) {
	for i := range a.state.Tasks {
		if a.state.Tasks[i].ID == id {
			fn(&a.state.Tasks[i])
			return
		}
	}
}

func (a *App) deleteTask(id int64) {
	var out []Task
	for _, t := range a.state.Tasks {
		if t.ID == id || t.ParentID == id {
			continue
		}
		out = append(out, t)
	}
	a.state.Tasks = out
}

func (a *App) hasChildren(id int64) bool {
	for _, t := range a.state.Tasks {
		if t.ParentID == id {
			return true
		}
	}
	return false
}

func (a *App) startEdit(taskID int64, kind string, r RECT) {
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in startEdit task=%d kind=%s: %v", taskID, kind, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.setStatus("Ошибка редактирования записана в лог")
			invalidate()
		}
	}()
	a.finishEdit(true)
	text := ""
	for _, t := range a.state.Tasks {
		if t.ID == taskID {
			switch kind {
			case "due":
				text = t.DueHHMM
			case "description":
				text = t.Description
			default:
				text = t.Text
			}
			break
		}
	}
	a.editActive = true
	a.editTaskID = taskID
	a.editKind = kind
	a.editText = text
	a.editOriginal = text
	a.editReplaceOnType = true
	a.editCreatedNew = false
	a.windowActive = true
	applyAlpha()
	logf("edit begin task=%d field=%s original_len=%d replace_on_type=%v", taskID, kind, len([]rune(text)), a.editReplaceOnType)
	procSetFocus.Call(uintptr(a.hwnd))
	a.setStatus("Редактирование: Enter - сохранить, Esc - отменить")
	invalidate()
}

func (a *App) finishEdit(save bool) {
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in finishEdit task=%d kind=%s save=%v: %v", a.editTaskID, a.editKind, save, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.editText = ""
			a.editOriginal = ""
			a.editReplaceOnType = false
			a.editCreatedNew = false
			a.setStatus("Ошибка сохранения редактирования записана в лог")
			invalidate()
		}
	}()
	if !a.editActive {
		return
	}
	taskID := a.editTaskID
	kind := a.editKind
	text := a.editText
	createdNew := a.editCreatedNew
	logf("edit finish begin task=%d kind=%s save=%v len=%d", taskID, kind, save, len([]rune(text)))
	a.editActive = false
	a.editTaskID = 0
	a.editKind = ""
	a.editText = ""
	a.editOriginal = ""
	a.editReplaceOnType = false
	a.editCreatedNew = false
	if !save {
		if createdNew {
			a.deleteTask(taskID)
			a.scheduleSave("edit_cancel_new")
		}
		applyAlpha()
		logf("edit cancel task=%d kind=%s removed_draft=%v", taskID, kind, createdNew)
		invalidate()
		return
	}
	trimmed := strings.TrimSpace(text)
	if createdNew && trimmed == "" {
		a.deleteTask(taskID)
		a.scheduleSave("edit_empty_new")
		invalidate()
		return
	}
	a.updateTask(taskID, func(t *Task) {
		switch kind {
		case "due":
			trimmed = strings.TrimSpace(text)
			if trimmed == "" || validHHMM(trimmed) {
				t.DueHHMM = trimmed
				t.Ack = false
				t.Blink = false
			} else {
				a.setStatus("Время нужно указать в формате HH:MM")
			}
		case "description":
			t.Description = strings.TrimSpace(text)
		default:
			trimmed = strings.TrimSpace(text)
			if trimmed != "" {
				t.Text = trimmed
			}
		}
	})
	a.scheduleSave("edit_commit_" + kind)
	applyAlpha()
	logf("edit finish end task=%d kind=%s save=%v", taskID, kind, save)
	invalidate()
}

func (a *App) handleKeyDown(wParam uintptr) bool {
	if !a.editActive {
		return false
	}
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in handleKeyDown key=%d task=%d kind=%s: %v", wParam, a.editTaskID, a.editKind, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.setStatus("Ошибка ввода записана в лог")
			invalidate()
		}
	}()
	debugInputf("edit keydown key=%d task=%d kind=%s replace=%v", wParam, a.editTaskID, a.editKind, a.editReplaceOnType)
	if wParam == VK_V && ctrlDown() {
		clip, err := clipboardText()
		if err == nil && clip != "" {
			if a.editReplaceOnType {
				a.editText = ""
				a.editReplaceOnType = false
			}
			if a.editKind == "text" || a.editKind == "due" {
				title, _ := deriveTitleAndDescription(clip)
				clip = title
			}
			a.editText += clip
			if len([]rune(a.editText)) > 4000 {
				a.editText = string([]rune(a.editText)[:4000])
			}
			invalidate()
		} else if err != nil {
			logf("clipboard paste failed err=%v", err)
		}
		return true
	}
	switch wParam {
	case VK_RETURN:
		a.finishEdit(true)
		return true
	case VK_ESCAPE:
		a.finishEdit(false)
		return true
	case VK_BACK:
		if a.editReplaceOnType {
			a.editText = ""
			a.editReplaceOnType = false
		} else {
			runes := []rune(a.editText)
			if len(runes) > 0 {
				a.editText = string(runes[:len(runes)-1])
			}
		}
		invalidate()
		return true
	case VK_DELETE:
		if a.editReplaceOnType {
			a.editText = ""
			a.editReplaceOnType = false
		}
		invalidate()
		return true
	}
	return true
}

func (a *App) handleChar(wParam uintptr) bool {
	if !a.editActive {
		return false
	}
	defer func() {
		if rec := recover(); rec != nil {
			logf("panic in handleChar char=%d task=%d kind=%s: %v", wParam, a.editTaskID, a.editKind, rec)
			logf("stack:\n%s", string(debug.Stack()))
			a.editActive = false
			a.setStatus("Ошибка ввода записана в лог")
			invalidate()
		}
	}()
	ch := rune(wParam)
	if ch < 32 {
		return true
	}
	debugInputf("edit char code=%d task=%d kind=%s len=%d replace=%v", wParam, a.editTaskID, a.editKind, len([]rune(a.editText)), a.editReplaceOnType)
	if a.editReplaceOnType {
		a.editText = ""
		a.editReplaceOnType = false
	}
	if a.editKind == "due" {
		if len([]rune(a.editText)) >= 5 {
			return true
		}
		if (ch < '0' || ch > '9') && ch != ':' {
			return true
		}
	} else {
		limit := 240
		if a.editKind == "description" {
			limit = 4000
		}
		if len([]rune(a.editText)) >= limit {
			return true
		}
	}
	a.editText += string(ch)
	invalidate()
	return true
}

func (a *App) scheduleSave(reason string) {
	if a == nil || a.hwnd == 0 {
		saveState(a.state)
		return
	}
	a.lastSaveReason = reason
	if !a.saveScheduled {
		a.saveScheduled = true
	}
	procSetTimer.Call(uintptr(a.hwnd), TIMER_SAVE, 350, 0)
}

func ctrlDown() bool {
	r, _, _ := procGetKeyState.Call(VK_CONTROL)
	return int16(r&0xffff) < 0
}

func clipboardText() (string, error) {
	avail, _, _ := procIsClipboardFormatAvailable.Call(CF_UNICODETEXT)
	if avail == 0 {
		return "", fmt.Errorf("unicode text not available")
	}
	r, _, err := procOpenClipboard.Call(0)
	if r == 0 {
		return "", err
	}
	defer procCloseClipboard.Call()
	h, _, err := procGetClipboardData.Call(CF_UNICODETEXT)
	if h == 0 {
		return "", err
	}
	ptr, _, err := procGlobalLock.Call(h)
	if ptr == 0 {
		return "", err
	}
	defer procGlobalUnlock.Call(h)
	var buf []uint16
	for i := uintptr(0); i < 8000*2; i += 2 {
		v := *(*uint16)(unsafe.Pointer(ptr + i))
		if v == 0 {
			break
		}
		buf = append(buf, v)
	}
	out := syscall.UTF16ToString(buf)
	out = strings.ReplaceAll(out, "\r\n", "\n")
	out = strings.TrimSpace(out)
	return out, nil
}

func deriveTitleAndDescription(clip string) (string, string) {
	clip = strings.TrimSpace(strings.ReplaceAll(clip, "\r\n", "\n"))
	if clip == "" {
		return "", ""
	}
	line := strings.TrimSpace(strings.SplitN(clip, "\n", 2)[0])
	r := []rune(line)
	if len(r) > 80 {
		line = string(r[:80]) + "..."
	}
	return line, clip
}

func (a *App) drawTaskDetails(hdc HDC, rc RECT, top int32, task Task) int32 {
	s := a.state.Settings
	tc := a.effectiveTextColor()
	h := a.scale(86)
	panel := RECT{Left: a.scale(34), Top: top, Right: rc.Right - a.scale(10), Bottom: top + h}
	if s.ShowBorders {
		drawBorder(hdc, panel, s.BorderColor)
	}
	x := panel.Left + a.scale(8)
	y := panel.Top + a.scale(6)
	drawText(hdc, "Описание", RECT{Left: x, Top: y, Right: panel.Right - a.scale(8), Bottom: y + a.scale(22)}, tc, a.font(max(13, s.FontSize-5)), true, s.Shadow, s.Outline, DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
	y += a.scale(24)
	descR := RECT{Left: x, Top: y, Right: panel.Right - a.scale(8), Bottom: panel.Bottom - a.scale(8)}
	a.addAction(descR, "edit_description", task.ID, 0)
	desc := task.Description
	if desc == "" {
		desc = "Кликните, чтобы добавить описание"
	}
	if a.editActive && a.editTaskID == task.ID && a.editKind == "description" {
		desc = a.editText
		if a.blinkOn {
			desc += "|"
		}
		drawBorder(hdc, descR, tc)
	}
	drawText(hdc, desc, descR, tc, a.font(max(13, s.FontSize-5)), false, false, false, DT_LEFT|DT_WORDBREAK|DT_END_ELLIPSIS|DT_NOPREFIX)
	return h
}

func minInt(a, b int) int {
	if a < b {
		return a
	}
	return b
}

func (a *App) checkDue() {
	now := time.Now().Format("15:04")
	changed := false
	for i := range a.state.Tasks {
		t := &a.state.Tasks[i]
		if t.Done || t.DueHHMM == "" || t.Ack {
			continue
		}
		if now >= t.DueHHMM {
			if !t.Blink {
				changed = true
			}
			t.Blink = true
		}
	}
	if changed {
		a.scheduleSave("due_changed")
	}
}

func (a *App) export(days int) {
	desktop := filepath.Join(os.Getenv("USERPROFILE"), "Desktop")
	if _, err := os.Stat(desktop); err != nil {
		desktop = os.Getenv("USERPROFILE")
	}
	name := fmt.Sprintf("TaskOverlay_export_%s.txt", time.Now().Format("20060102_1504"))
	path := filepath.Join(desktop, name)
	var b strings.Builder
	b.WriteString("TaskOverlay export\r\n")
	b.WriteString(time.Now().Format("2006-01-02 15:04"))
	b.WriteString("\r\n\r\n")
	var since time.Time
	if days > 0 {
		since = time.Now().AddDate(0, 0, -days)
	}
	for _, t := range a.state.Tasks {
		if days > 0 && t.CreatedAt.Before(since) {
			continue
		}
		prefix := "[ ] "
		if t.Done {
			prefix = "[x] "
		}
		if t.ParentID != 0 {
			prefix = "  - " + prefix
		}
		b.WriteString(prefix)
		b.WriteString(t.Text)
		if t.Priority > 0 {
			b.WriteString(" | P" + strconv.Itoa(t.Priority))
		}
		if t.InWork {
			b.WriteString(" | в работе")
		}
		if t.DueHHMM != "" {
			b.WriteString(" | " + t.DueHHMM)
		}
		if strings.TrimSpace(t.Description) != "" {
			b.WriteString(" | есть описание")
		}
		b.WriteString(" | создано: " + t.CreatedAt.Format("2006-01-02 15:04"))
		if t.DoneAt != nil {
			b.WriteString(" | выполнено: " + t.DoneAt.Format("2006-01-02 15:04"))
		}
		b.WriteString("\r\n")
	}
	if err := os.WriteFile(path, []byte(b.String()), 0644); err != nil {
		a.setStatus("Не удалось выгрузить файл")
	} else {
		a.setStatus("Экспорт создан на рабочем столе: " + name)
	}
}

func (a *App) setStatus(s string) {
	a.status = s
	a.statusUntil = time.Now().Add(4 * time.Second)
	procSetTimer.Call(uintptr(a.hwnd), TIMER_STATUS, 1000, 0)
}

func (a *App) captureWindowRect() {
	if a == nil || a.hwnd == 0 {
		return
	}
	var r RECT
	procGetWindowRect.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&r)))
	if r.Right > r.Left && r.Bottom > r.Top {
		a.state.Settings.X = r.Left
		a.state.Settings.Y = r.Top
		a.state.Settings.W = r.Right - r.Left
		a.state.Settings.H = r.Bottom - r.Top
	}
}

func loadState() State {
	st := defaultState()
	path := statePath()
	if data, err := os.ReadFile(path); err == nil {
		if err := json.Unmarshal(data, &st); err != nil {
			logf("state read failed path=%s err=%v", path, err)
			backupBytes("state_corrupt", data)
			st = defaultState()
		} else {
			logf("state loaded path=%s tasks=%d schema=%d app_version=%s", path, len(st.Tasks), st.SchemaVersion, st.AppVersion)
		}
	} else if os.IsNotExist(err) {
		if migrated, ok := loadMigratedState(); ok {
			st = migrated
			logf("state migrated tasks=%d", len(st.Tasks))
		} else {
			logf("state not found, using defaults")
		}
	} else {
		logf("state read error path=%s err=%v", path, err)
	}

	if st.SchemaVersion > SchemaVersion {
		logf("state schema is newer than app supports schema=%d supported=%d", st.SchemaVersion, SchemaVersion)
		backupCurrentState("newer_schema_" + strconv.Itoa(st.SchemaVersion))
	}
	oldVersion := st.AppVersion
	oldSchema := st.SchemaVersion
	normalizeState(&st, oldSchema)
	if oldVersion != "" && oldVersion != AppVersion {
		backupCurrentState("before_upgrade_" + safeName(oldVersion) + "_to_" + safeName(AppVersion))
		logf("upgrade detected from=%s to=%s", oldVersion, AppVersion)
	}
	st.SchemaVersion = SchemaVersion
	st.AppVersion = AppVersion
	saveState(st)
	return st
}

func loadMigratedState() (State, bool) {
	candidates := []string{
		filepath.Join(appDataDir(), "state_v3.json"),
	}
	for _, path := range candidates {
		data, err := os.ReadFile(path)
		if err != nil {
			continue
		}
		var st State
		if err := json.Unmarshal(data, &st); err != nil {
			logf("migration skipped, unreadable path=%s err=%v", path, err)
			backupBytes("migration_corrupt", data)
			continue
		}
		backupBytes("migrated_from_"+filepath.Base(path), data)
		return st, true
	}
	return State{}, false
}

func normalizeState(st *State, oldSchema int) {
	if st.Settings.W < 300 {
		st.Settings.W = 620
	}
	if st.Settings.H < 250 {
		st.Settings.H = 520
	}
	if st.Settings.W > 1400 {
		st.Settings.W = 620
	}
	if st.Settings.H > 1000 {
		st.Settings.H = 520
	}
	if st.Settings.X < -2000 || st.Settings.X > 6000 {
		st.Settings.X = 1200
	}
	if st.Settings.Y < -2000 || st.Settings.Y > 4000 {
		st.Settings.Y = 120
	}
	if st.Settings.FontSize < 10 {
		st.Settings.FontSize = 20
	}
	if st.Settings.FontSize > 44 {
		st.Settings.FontSize = 20
	}
	if st.Settings.Alpha < 40 {
		st.Settings.Alpha = 230
	}
	if st.Settings.BgAlpha == 0 {
		st.Settings.BgAlpha = st.Settings.Alpha
	}
	if st.Settings.BgAlpha < 35 {
		st.Settings.BgAlpha = 170
	}
	if st.Settings.TextAlpha == 0 {
		st.Settings.TextAlpha = 255
	}
	if oldSchema < 13 {
		st.Settings.CompletedExpanded = true
	}
	if st.NextID < 1 {
		st.NextID = 1
	}
	for i := range st.Tasks {
		if st.Tasks[i].ID >= st.NextID {
			st.NextID = st.Tasks[i].ID + 1
		}
		if st.Tasks[i].CreatedAt.IsZero() {
			st.Tasks[i].CreatedAt = time.Now()
		}
	}
}

func defaultState() State {
	now := time.Now()
	return State{
		SchemaVersion: SchemaVersion,
		AppVersion:    AppVersion,
		NextID:        3,
		Settings:      Settings{X: 1200, Y: 120, W: 620, H: 520, BgColor: rgb(22, 25, 30), TextColor: rgb(255, 232, 120), BorderColor: rgb(150, 150, 150), Alpha: 170, BgAlpha: 170, TextAlpha: 255, FontSize: 20, Bold: false, Shadow: true, Outline: false, ShowBorders: false, DoneStyle: 0, CollapseDone: false, CompletedExpanded: true},
		Tasks: []Task{
			{ID: 1, Text: "Редактируйте текст задачи кликом", CreatedAt: now, Expanded: true},
			{ID: 2, Text: "Кнопка + добавляет задачу", CreatedAt: now, Expanded: true},
		},
	}
}

func saveState(st State) {
	path := statePath()
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		logf("save mkdir failed path=%s err=%v", path, err)
		return
	}
	st.SchemaVersion = SchemaVersion
	st.AppVersion = AppVersion
	data, err := json.MarshalIndent(st, "", "  ")
	if err != nil {
		logf("save marshal failed err=%v", err)
		return
	}
	if err := saveBytesAtomic(path, data); err != nil {
		logf("save atomic failed path=%s err=%v", path, err)
		return
	}
}

func saveBytesAtomic(path string, data []byte) error {
	dir := filepath.Dir(path)
	if err := os.MkdirAll(dir, 0755); err != nil {
		return err
	}
	tmp, err := os.CreateTemp(dir, filepath.Base(path)+"-*.tmp")
	if err != nil {
		return err
	}
	tmpName := tmp.Name()
	defer os.Remove(tmpName)
	if _, err := tmp.Write(data); err != nil {
		_ = tmp.Close()
		return err
	}
	_ = tmp.Sync()
	if err := tmp.Close(); err != nil {
		return err
	}
	if _, err := os.Stat(path); os.IsNotExist(err) {
		return os.Rename(tmpName, path)
	}
	bak := path + ".bak"
	_ = os.Remove(bak)
	if err := replaceFileWindows(path, tmpName, bak); err != nil {
		// Fallback for environments where ReplaceFileW is blocked.
		if err2 := os.Rename(tmpName, path); err2 != nil {
			return fmt.Errorf("replacefile=%v rename=%v", err, err2)
		}
	}
	return nil
}

func replaceFileWindows(dst, src, bak string) error {
	dst16, err := syscall.UTF16PtrFromString(dst)
	if err != nil {
		return err
	}
	src16, err := syscall.UTF16PtrFromString(src)
	if err != nil {
		return err
	}
	var bakPtr *uint16
	if bak != "" {
		bakPtr, _ = syscall.UTF16PtrFromString(bak)
	}
	r, _, e := procReplaceFileW.Call(
		uintptr(unsafe.Pointer(dst16)),
		uintptr(unsafe.Pointer(src16)),
		uintptr(unsafe.Pointer(bakPtr)),
		0, 0, 0,
	)
	if r == 0 {
		if e != syscall.Errno(0) {
			return e
		}
		return fmt.Errorf("ReplaceFileW failed")
	}
	return nil
}

func statePath() string {
	return filepath.Join(appDataDir(), "state.json")
}

func appDataDir() string {
	base := os.Getenv("APPDATA")
	if base == "" {
		base = os.TempDir()
	}
	return filepath.Join(base, "TaskOverlay")
}

func logsDir() string {
	return filepath.Join(appDataDir(), "logs")
}

func backupDir() string {
	return filepath.Join(appDataDir(), "backup")
}

func backupBytes(prefix string, data []byte) {
	_ = os.MkdirAll(backupDir(), 0755)
	path := filepath.Join(backupDir(), safeName(prefix)+"_"+time.Now().Format("20060102_150405")+".json")
	if err := os.WriteFile(path, data, 0644); err != nil {
		logf("backup write failed path=%s err=%v", path, err)
	} else {
		logf("backup created path=%s", path)
	}
}

func backupCurrentState(prefix string) {
	data, err := os.ReadFile(statePath())
	if err != nil {
		return
	}
	backupBytes(prefix, data)
}

func safeName(s string) string {
	replacer := strings.NewReplacer("\\", "_", "/", "_", ":", "_", "*", "_", "?", "_", "\"", "_", "<", "_", ">", "_", "|", "_", " ", "_")
	return replacer.Replace(s)
}

func initLogger() {
	_ = os.MkdirAll(logsDir(), 0755)
	logFilePath = filepath.Join(logsDir(), "TaskOverlay_"+time.Now().Format("20060102")+".log")
	logf("logger initialized")
}

func initCrashOutput() {
	path := filepath.Join(logsDir(), "TaskOverlay_crash_"+time.Now().Format("20060102")+".log")
	f, err := os.OpenFile(path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err != nil {
		logf("crash output open failed path=%s err=%v", path, err)
		return
	}
	crashFile = f
	if err := debug.SetCrashOutput(f, debug.CrashOptions{}); err != nil {
		logf("crash output setup failed path=%s err=%v", path, err)
		return
	}
	logf("crash output enabled path=%s", path)
}

func debugInputf(format string, args ...interface{}) {
	if DebugInput {
		logf(format, args...)
	}
}

func logf(format string, args ...interface{}) {
	if logFilePath == "" {
		base := os.Getenv("APPDATA")
		if base == "" {
			base = os.TempDir()
		}
		logFilePath = filepath.Join(base, "TaskOverlay", "logs", "TaskOverlay_"+time.Now().Format("20060102")+".log")
	}
	line := time.Now().Format("2006-01-02 15:04:05.000") + " " + fmt.Sprintf(format, args...) + "\r\n"
	logMu.Lock()
	defer logMu.Unlock()
	_ = os.MkdirAll(filepath.Dir(logFilePath), 0755)
	f, err := os.OpenFile(logFilePath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0644)
	if err != nil {
		return
	}
	_, _ = f.WriteString(line)
	_ = f.Close()
}

type SessionInfo struct {
	PID       int       `json:"pid"`
	Version   string    `json:"version"`
	StartedAt time.Time `json:"started_at"`
	Clean     bool      `json:"clean"`
}

func sessionPath() string { return filepath.Join(appDataDir(), "session.json") }

func markSessionStart() {
	_ = os.MkdirAll(appDataDir(), 0755)
	sessionStartedAt = time.Now()
	info := SessionInfo{PID: os.Getpid(), Version: AppVersion, StartedAt: sessionStartedAt, Clean: false}
	data, _ := json.MarshalIndent(info, "", "  ")
	_ = os.WriteFile(sessionPath(), data, 0644)
}

func markSessionEnd(clean bool) {
	started := sessionStartedAt
	if started.IsZero() {
		started = time.Now()
	}
	info := SessionInfo{PID: os.Getpid(), Version: AppVersion, StartedAt: started, Clean: clean}
	data, _ := json.MarshalIndent(info, "", "  ")
	_ = os.WriteFile(sessionPath(), data, 0644)
}

func readSessionInfo() (SessionInfo, bool) {
	data, err := os.ReadFile(sessionPath())
	if err != nil {
		return SessionInfo{}, false
	}
	var info SessionInfo
	if json.Unmarshal(data, &info) != nil {
		return SessionInfo{}, false
	}
	return info, true
}

func checkPreviousSession() (bool, bool) {
	info, ok := readSessionInfo()
	if !ok || info.Clean {
		return true, true
	}
	if isProcessRunning(info.PID) {
		logf("previous TaskOverlay process is still running pid=%d version=%s started=%s", info.PID, info.Version, info.StartedAt.Format(time.RFC3339))
		messageBox("TaskOverlay уже запущен. Закройте старое окно перед запуском новой версии.", "TaskOverlay")
		return true, false
	}
	logf("previous session was not clean pid=%d version=%s started=%s", info.PID, info.Version, info.StartedAt.Format(time.RFC3339))
	return false, true
}

func acquireSingleInstance() bool {
	name := utf16Ptr("Global\\TaskOverlay.SingleInstance")
	h, _, _ := procCreateMutexW.Call(0, 1, uintptr(unsafe.Pointer(name)))
	if h == 0 {
		logf("CreateMutexW failed, continuing without mutex")
		return true
	}
	instanceMutex = h
	lastErr, _, _ := procGetLastError.Call()
	if lastErr == ERROR_ALREADY_EXISTS {
		logf("another TaskOverlay instance detected by mutex")
		messageBox("TaskOverlay уже запущен. Закройте старое окно перед запуском новой версии.", "TaskOverlay")
		return false
	}
	logf("single instance lock acquired")
	return true
}

func isProcessRunning(pid int) bool {
	if pid <= 0 || pid == os.Getpid() {
		return false
	}
	out, err := exec.Command("tasklist", "/FI", fmt.Sprintf("PID eq %d", pid), "/FO", "CSV", "/NH").CombinedOutput()
	if err != nil {
		logf("tasklist check failed pid=%d err=%v", pid, err)
		return false
	}
	return strings.Contains(string(out), fmt.Sprintf("\"%d\"", pid)) || strings.Contains(string(out), fmt.Sprintf(",%d,", pid))
}

func messageBox(text, title string) {
	procMessageBoxW.Call(0, uintptr(unsafe.Pointer(utf16Ptr(text))), uintptr(unsafe.Pointer(utf16Ptr(title))), MB_OK|MB_ICONWARNING)
}

func (a *App) exportDiagnostics() {
	defer func() {
		if r := recover(); r != nil {
			logf("panic in exportDiagnostics: %v", r)
			logf("stack:\n%s", string(debug.Stack()))
			a.setStatus("Диагностика не создана. Ошибка записана в лог")
		}
	}()
	logf("diagnostics begin")
	path := diagnosticsPath()
	logf("diagnostics path=%s", path)
	var b strings.Builder
	b.WriteString("TaskOverlay diagnostics\r\n")
	b.WriteString("version: " + AppVersion + "\r\n")
	b.WriteString("time: " + time.Now().Format("2006-01-02 15:04:05") + "\r\n")
	b.WriteString("go: " + runtime.Version() + "\r\n")
	b.WriteString("os: " + runtime.GOOS + " " + runtime.GOARCH + "\r\n")
	if exe, err := os.Executable(); err == nil {
		b.WriteString("exe: " + exe + "\r\n")
	} else {
		b.WriteString("exe_error: " + err.Error() + "\r\n")
	}
	b.WriteString("app_data: " + appDataDir() + "\r\n")
	b.WriteString("state_path: " + statePath() + "\r\n")
	b.WriteString("session_path: " + sessionPath() + "\r\n")
	if info, ok := readSessionInfo(); ok {
		b.WriteString(fmt.Sprintf("session: pid=%d version=%s clean=%v started=%s\r\n", info.PID, info.Version, info.Clean, info.StartedAt.Format(time.RFC3339)))
	} else {
		b.WriteString("session: not available\r\n")
	}
	b.WriteString("log_path: " + logFilePath + "\r\n")
	logf("diagnostics state read begin")
	b.WriteString("\r\nSTATE\r\n")
	if data, err := json.MarshalIndent(a.state, "", "  "); err == nil {
		b.Write(data)
	} else {
		b.WriteString("state marshal error: " + err.Error())
	}
	logf("diagnostics log tail begin")
	b.WriteString("\r\n\r\nRECENT LOG\r\n")
	b.WriteString(readLastLogLinesSafe(logFilePath, 128*1024))
	logf("diagnostics write begin")
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		logf("diagnostics mkdir failed path=%s err=%v", filepath.Dir(path), err)
		a.setStatus("Не удалось создать папку диагностики")
		return
	}
	if err := saveBytesAtomic(path, []byte(b.String())); err != nil {
		logf("diagnostics write failed path=%s err=%v", path, err)
		a.setStatus("Не удалось сохранить диагностику")
		return
	}
	logf("diagnostics exported path=%s", path)
	a.setStatus("Диагностика создана на рабочем столе")
}

func diagnosticsPath() string {
	desktop := filepath.Join(os.Getenv("USERPROFILE"), "Desktop")
	if _, err := os.Stat(desktop); err != nil {
		desktop = os.Getenv("USERPROFILE")
	}
	if desktop == "" {
		desktop = appDataDir()
	}
	name := "TaskOverlay_diagnostics_" + time.Now().Format("20060102_150405") + ".txt"
	return filepath.Join(desktop, name)
}

func readLastLogLines(path string, limit int) string {
	return readLastLogLinesSafe(path, int64(limit)*1024)
}

func readLastLogLinesSafe(path string, maxBytes int64) (out string) {
	defer func() {
		if r := recover(); r != nil {
			out = fmt.Sprintf("log tail panic: %v\r\n", r)
			logf("panic in readLastLogLinesSafe: %v", r)
		}
	}()
	if path == "" {
		return "log path is empty\r\n"
	}
	if maxBytes < 4096 {
		maxBytes = 4096
	}
	f, err := os.Open(path)
	if err != nil {
		return "log read error: " + err.Error() + "\r\n"
	}
	defer f.Close()
	st, err := f.Stat()
	if err != nil {
		return "log stat error: " + err.Error() + "\r\n"
	}
	start := int64(0)
	if st.Size() > maxBytes {
		start = st.Size() - maxBytes
	}
	buf := make([]byte, st.Size()-start)
	n, err := f.ReadAt(buf, start)
	if err != nil && err != io.EOF {
		return "log read tail error: " + err.Error() + "\r\n"
	}
	buf = buf[:n]
	text := strings.ReplaceAll(string(buf), "\r\n", "\n")
	text = strings.ReplaceAll(text, "\r", "\n")
	lines := strings.Split(text, "\n")
	const maxLines = 220
	if len(lines) > maxLines {
		lines = lines[len(lines)-maxLines:]
	}
	return strings.Join(lines, "\r\n")
}

func (a *App) openLogFolder() {
	logf("open logs folder")
	_ = os.MkdirAll(logsDir(), 0755)
	if err := exec.Command("explorer.exe", logsDir()).Start(); err != nil {
		logf("open logs failed err=%v", err)
		a.setStatus("Не удалось открыть папку логов")
	}
}

func (a *App) resetWindow() {
	logf("reset window")
	a.state.Settings.X = 1200
	a.state.Settings.Y = 120
	a.state.Settings.W = 620
	a.state.Settings.H = 520
	procSetWindowPos.Call(uintptr(a.hwnd), 0, uintptr(a.state.Settings.X), uintptr(a.state.Settings.Y), uintptr(a.state.Settings.W), uintptr(a.state.Settings.H), 0)
	saveState(a.state)
	a.setStatus("Положение окна сброшено")
}

func createShortcutSafe() {
	exePath, err := os.Executable()
	if err != nil {
		logf("shortcut skipped, executable error=%v", err)
		return
	}
	desktop := filepath.Join(os.Getenv("USERPROFILE"), "Desktop")
	if _, err := os.Stat(desktop); err != nil {
		logf("shortcut skipped, desktop not found path=%s err=%v", desktop, err)
		return
	}
	lnk := filepath.Join(desktop, "TaskOverlay.lnk")
	dir := filepath.Dir(exePath)
	icon := filepath.Join(dir, "TaskOverlay.ico")
	if _, err := os.Stat(icon); err != nil {
		icon = exePath
	}
	ps := fmt.Sprintf("$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%s');$s.TargetPath='%s';$s.WorkingDirectory='%s';$s.IconLocation='%s';$s.Save()", psq(lnk), psq(exePath), psq(dir), psq(icon))
	if err := exec.Command("powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps).Start(); err != nil {
		logf("shortcut update failed path=%s err=%v", lnk, err)
	} else {
		logf("shortcut update requested path=%s target=%s", lnk, exePath)
	}
}

func psq(s string) string { return strings.ReplaceAll(s, "'", "''") }

func applyAlpha() {
	if app == nil || app.hwnd == 0 {
		return
	}
	alpha := app.state.Settings.BgAlpha
	if alpha == 0 {
		alpha = app.state.Settings.Alpha
	}
	if app.windowActive || app.editActive {
		alpha = 255
	}
	if alpha < 35 {
		alpha = 35
	}
	procSetLayeredWindowAttributes.Call(uintptr(app.hwnd), 0, uintptr(alpha), LWA_ALPHA)
}

func applyTopMost() {
	if app == nil || app.hwnd == 0 {
		return
	}
	const hwndTopMost = ^uintptr(0) // HWND_TOPMOST = -1
	procSetWindowPos.Call(uintptr(app.hwnd), hwndTopMost, 0, 0, 0, 0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)
}

func (a *App) effectiveTextColor() uint32 {
	if a == nil {
		return rgb(245, 245, 245)
	}
	if a.windowActive || a.editActive {
		return a.state.Settings.TextColor
	}
	alpha := a.state.Settings.TextAlpha
	if alpha == 0 {
		alpha = 255
	}
	return blendColor(a.state.Settings.BgColor, a.state.Settings.TextColor, alpha)
}

func blendColor(bg, fg uint32, alpha byte) uint32 {
	br := int(bg & 0xff)
	bgG := int((bg >> 8) & 0xff)
	bb := int((bg >> 16) & 0xff)
	fr := int(fg & 0xff)
	fgG := int((fg >> 8) & 0xff)
	fb := int((fg >> 16) & 0xff)
	a := int(alpha)
	r := byte((fr*a + br*(255-a)) / 255)
	g := byte((fgG*a + bgG*(255-a)) / 255)
	b := byte((fb*a + bb*(255-a)) / 255)
	return rgb(r, g, b)
}

func fillRect(hdc HDC, r RECT, color uint32) {
	br, _, _ := procCreateSolidBrush.Call(uintptr(color))
	procFillRect.Call(uintptr(hdc), uintptr(unsafe.Pointer(&r)), br)
	procDeleteObject.Call(br)
}

func drawBorder(hdc HDC, r RECT, color uint32) {
	pen, _, _ := procCreatePen.Call(PS_SOLID, 1, uintptr(color))
	oldPen, _, _ := procSelectObject.Call(uintptr(hdc), pen)
	br, _, _ := procGetStockObject.Call(NULL_BRUSH)
	oldBr, _, _ := procSelectObject.Call(uintptr(hdc), br)
	procRectangle.Call(uintptr(hdc), uintptr(r.Left), uintptr(r.Top), uintptr(r.Right), uintptr(r.Bottom))
	procSelectObject.Call(uintptr(hdc), oldPen)
	procSelectObject.Call(uintptr(hdc), oldBr)
	procDeleteObject.Call(pen)
}

func drawStrike(hdc HDC, r RECT, color uint32) {
	pen, _, _ := procCreatePen.Call(PS_SOLID, 1, uintptr(color))
	oldPen, _, _ := procSelectObject.Call(uintptr(hdc), pen)
	y := (r.Top + r.Bottom) / 2
	procMoveToEx.Call(uintptr(hdc), uintptr(r.Left), uintptr(y), 0)
	procLineTo.Call(uintptr(hdc), uintptr(r.Right), uintptr(y))
	procSelectObject.Call(uintptr(hdc), oldPen)
	procDeleteObject.Call(pen)
}

func drawButton(hdc HDC, text string, r RECT, color uint32, size int32) {
	drawText(hdc, text, r, color, size, true, false, false, DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
}

func drawButtonSafe(hdc HDC, text string, r RECT, color uint32, size int32) {
	drawTextSafe(hdc, text, r, color, size, true, false, false, DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX)
}

func drawTextSafe(hdc HDC, text string, r RECT, color uint32, size int32, bold, shadow, outline bool, flags uint32) {
	if text == "" {
		return
	}
	if r.Right <= r.Left || r.Bottom <= r.Top {
		return
	}
	if size < 8 {
		size = 14
	}
	if size > 48 {
		size = 20
	}
	weight := int32(FW_NORMAL)
	if bold {
		weight = FW_BOLD
	}
	face16, err := syscall.UTF16FromString("Segoe UI")
	if err != nil {
		return
	}
	text16, err := syscall.UTF16FromString(text)
	if err != nil {
		return
	}
	font, _, _ := procCreateFontW.Call(uintptr(uint32(-size)), 0, 0, 0, uintptr(weight), 0, 0, 0, 1, 0, 0, 5, 0, uintptr(unsafe.Pointer(&face16[0])))
	if font == 0 {
		return
	}
	oldFont, _, _ := procSelectObject.Call(uintptr(hdc), font)
	procSetBkMode.Call(uintptr(hdc), TRANSPARENT)
	procSetTextColor.Call(uintptr(hdc), uintptr(color))
	procDrawTextW.Call(uintptr(hdc), uintptr(unsafe.Pointer(&text16[0])), uintptr(0xFFFFFFFF), uintptr(unsafe.Pointer(&r)), uintptr(flags))
	procSelectObject.Call(uintptr(hdc), oldFont)
	procDeleteObject.Call(font)
	runtime.KeepAlive(face16)
	runtime.KeepAlive(text16)
}

func drawText(hdc HDC, text string, r RECT, color uint32, size int32, bold, shadow, outline bool, flags uint32) {
	if text == "" {
		return
	}
	if r.Right <= r.Left || r.Bottom <= r.Top {
		return
	}
	if shadow {
		rr := r
		rr.Left += 2
		rr.Top += 2
		rr.Right += 2
		rr.Bottom += 2
		drawTextSafe(hdc, text, rr, rgb(0, 0, 0), size, bold, false, false, flags)
	}
	if outline {
		offsets := [][2]int32{{-1, 0}, {1, 0}, {0, -1}, {0, 1}}
		for _, o := range offsets {
			rr := r
			rr.Left += o[0]
			rr.Right += o[0]
			rr.Top += o[1]
			rr.Bottom += o[1]
			drawTextSafe(hdc, text, rr, rgb(0, 0, 0), size, bold, false, false, flags)
		}
	}
	drawTextSafe(hdc, text, r, color, size, bold, false, false, flags)
}

func getWindowText(hwnd HWND) string {
	n, _, _ := procGetWindowTextLengthW.Call(uintptr(hwnd))
	buf := make([]uint16, n+2)
	procGetWindowTextW.Call(uintptr(hwnd), uintptr(unsafe.Pointer(&buf[0])), n+1)
	return syscall.UTF16ToString(buf)
}

func utf16Ptr(s string) *uint16 { p, _ := syscall.UTF16PtrFromString(s); return p }

func rgb(r, g, b byte) uint32 { return uint32(r) | uint32(g)<<8 | uint32(b)<<16 }

func adjustColor(c uint32, delta byte) uint32 {
	r := byte(c & 0xff)
	g := byte((c >> 8) & 0xff)
	b := byte((c >> 16) & 0xff)
	add := func(v byte) byte {
		if int(v)+int(delta) > 255 {
			return 255
		}
		return v + delta
	}
	return rgb(add(r), add(g), add(b))
}

func max(a, b int32) int32 {
	if a > b {
		return a
	}
	return b
}

func inRect(x, y int32, r RECT) bool {
	return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom
}

func getXY(lp uintptr) (int32, int32) {
	return int32(int16(lp & 0xffff)), int32(int16((lp >> 16) & 0xffff))
}

func hiword(v uintptr) uint16 { return uint16((v >> 16) & 0xffff) }

func invalidate() {
	if app != nil && app.hwnd != 0 {
		procInvalidateRect.Call(uintptr(app.hwnd), 0, 1)
	}
}

func priorityLabel(p int) string {
	switch p {
	case 1:
		return "P1"
	case 2:
		return "P2"
	case 3:
		return "P3"
	}
	return "P-"
}

func validHHMM(s string) bool {
	if len(s) != 5 || s[2] != ':' {
		return false
	}
	h, e1 := strconv.Atoi(s[:2])
	m, e2 := strconv.Atoi(s[3:])
	return e1 == nil && e2 == nil && h >= 0 && h <= 23 && m >= 0 && m <= 59
}

func (a *App) uiScale() float64 {
	w := a.state.Settings.W
	h := a.state.Settings.H
	if w <= 0 || h <= 0 {
		return 1
	}
	sx := float64(w) / float64(BaseW)
	sy := float64(h) / float64(BaseH)
	v := math.Min(sx, sy)
	if v < 0.65 {
		v = 0.65
	}
	if v > 2.2 {
		v = 2.2
	}
	return v
}

func (a *App) scale(v int32) int32 {
	if v == 0 {
		return 0
	}
	out := int32(math.Round(float64(v) * a.uiScale()))
	if out == 0 {
		if v > 0 {
			return 1
		}
		return -1
	}
	return out
}

func (a *App) font(v int32) int32 {
	out := a.scale(v)
	if out < 8 {
		out = 8
	}
	if out > 72 {
		out = 72
	}
	return out
}

func (a *App) drawResizeGrip(hdc HDC, rc RECT) {
	tc := a.effectiveTextColor()
	m := a.scale(8)
	for i := int32(0); i < 3; i++ {
		x1 := rc.Right - m - i*a.scale(6)
		y1 := rc.Bottom - a.scale(2)
		x2 := rc.Right - a.scale(2)
		y2 := rc.Bottom - m - i*a.scale(6)
		pen, _, _ := procCreatePen.Call(PS_SOLID, 1, uintptr(tc))
		oldPen, _, _ := procSelectObject.Call(uintptr(hdc), pen)
		procMoveToEx.Call(uintptr(hdc), uintptr(x1), uintptr(y1), 0)
		procLineTo.Call(uintptr(hdc), uintptr(x2), uintptr(y2))
		procSelectObject.Call(uintptr(hdc), oldPen)
		procDeleteObject.Call(pen)
	}
}

func (a *App) hitTestResize(lParam uintptr) uintptr {
	if a == nil || a.hwnd == 0 {
		return HTCLIENT
	}
	x := int32(int16(lParam & 0xffff))
	y := int32(int16((lParam >> 16) & 0xffff))
	var wr RECT
	procGetWindowRect.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&wr)))
	margin := a.scale(14)
	if margin < 10 {
		margin = 10
	}
	right := x >= wr.Right-margin && x <= wr.Right+margin
	bottom := y >= wr.Bottom-margin && y <= wr.Bottom+margin
	if right && bottom {
		return HTBOTTOMRIGHT
	}
	if right {
		return HTRIGHT
	}
	if bottom {
		return HTBOTTOM
	}
	return HTCLIENT
}

func (a *App) addAction(r RECT, kind string, taskID int64, value int) {
	a.actions = append(a.actions, Action{Rect: r, Kind: kind, TaskID: taskID, Value: value})
}
