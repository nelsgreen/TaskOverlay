//go:build windows

package main

import (
	"syscall"
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
	WS_EX_TOPMOST    = 0x00000008
	ES_AUTOHSCROLL   = 0x0080

	LWA_COLORKEY = 0x00000001

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

	TIMER_BLINK      = 1
	TIMER_STATUS     = 2
	TIMER_SAVE       = 3
	TIMER_HOVER_POLL = 4
	TIMER_PASSIVE    = 5

	WA_INACTIVE = 0

	ERROR_ALREADY_EXISTS = 183
	MB_OK                = 0
	MB_ICONWARNING       = 0x00000030
	CF_UNICODETEXT       = 13

	SWP_NOMOVE     = 0x0002
	SWP_NOSIZE     = 0x0001
	SWP_NOZORDER   = 0x0004
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
