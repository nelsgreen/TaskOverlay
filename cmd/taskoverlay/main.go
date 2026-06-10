//go:build windows

package main

import (
	"os"
	"runtime"
	"runtime/debug"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

const (
	AppVersion    = "14.0.0"
	SchemaVersion = 14
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
		uintptr(WS_POPUP|WS_VISIBLE|WS_CLIPCHILDREN),
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
	procSetTimer.Call(hwnd, TIMER_HOVER_POLL, 100, 0)

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
