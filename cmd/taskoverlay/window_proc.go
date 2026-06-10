//go:build windows

package main

import (
	"runtime/debug"
	"time"
	"unsafe"
)

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
			applyLayeredWindowMode()
			invalidate()
		}
		return 0
	case WM_SETFOCUS:
		if app != nil {
			app.windowActive = true
			applyLayeredWindowMode()
			invalidate()
		}
		return 0
	case WM_KILLFOCUS:
		if app != nil {
			app.windowActive = false
			applyLayeredWindowMode()
			invalidate()
		}
		return 0
	case WM_PAINT:
		app.paint()
		return 0
	case WM_TIMER:
		if wParam == TIMER_BLINK {
			applyTopMost()
			dueChanged := app.checkDue()
			needsBlinkPaint := app.editActive || app.hasBlinkingTasks()
			if needsBlinkPaint {
				app.blinkOn = !app.blinkOn
			}
			if dueChanged || needsBlinkPaint {
				invalidate()
			}
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
		} else if wParam == TIMER_HOVER_POLL {
			app.updateHoverState()
		} else if wParam == TIMER_PASSIVE {
			procKillTimer.Call(uintptr(hwnd), TIMER_PASSIVE)
			if !app.mouseInside && !app.passiveTransitionBlocked() {
				app.setOverlayMode(false, "hover_leave_delay")
			}
		}
		return 0
	case WM_COMMAND:
		return 0
	case WM_GETMINMAXINFO:
		if lParam != 0 {
			mmi := (*MINMAXINFO)(unsafe.Pointer(lParam))
			if app != nil && app.isActiveMode() {
				mmi.PtMinTrackSize.X = 420
				mmi.PtMinTrackSize.Y = 320
			} else {
				mmi.PtMinTrackSize.X = 80
				mmi.PtMinTrackSize.Y = 36
			}
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
		app.schedulePassiveMode()
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
		if !app.sizing && !app.modeChanging && app.isActiveMode() {
			app.scheduleSave("move")
		}
		return 0
	case WM_SIZE:
		app.captureWindowRect()
		invalidate()
		if !app.sizing && !app.modeChanging && app.isActiveMode() {
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
