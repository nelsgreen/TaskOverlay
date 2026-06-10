//go:build windows

package main

import (
	"runtime"
	"syscall"
	"time"
	"unsafe"
)

var passiveColorKey = rgb(1, 2, 3)

func (a *App) isActiveMode() bool {
	return a != nil && (a.overlayActive || a.editActive)
}

func applyLayeredWindowMode() {
	if app == nil || app.hwnd == 0 {
		return
	}
	procSetLayeredWindowAttributes.Call(uintptr(app.hwnd), uintptr(passiveColorKey), 255, LWA_COLORKEY)
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
	alpha := a.state.Settings.TextAlpha
	if alpha == 0 {
		alpha = 255
	}
	background := rgb(0, 0, 0)
	if a.isActiveMode() {
		background = a.activeBackgroundColor()
	}
	return blendColor(background, a.state.Settings.TextColor, alpha)
}

func (a *App) updateHoverState() {
	if a == nil || a.hwnd == 0 || a.sizing {
		return
	}
	var cursor POINT
	var window RECT
	okCursor, _, _ := procGetCursorPos.Call(uintptr(unsafe.Pointer(&cursor)))
	okWindow, _, _ := procGetWindowRect.Call(uintptr(a.hwnd), uintptr(unsafe.Pointer(&window)))
	if okCursor == 0 || okWindow == 0 {
		return
	}
	inside := cursor.X >= window.Left && cursor.X < window.Right && cursor.Y >= window.Top && cursor.Y < window.Bottom
	if inside {
		procKillTimer.Call(uintptr(a.hwnd), TIMER_PASSIVE)
		if !a.mouseInside || !a.overlayActive {
			a.mouseInside = true
			a.setOverlayMode(true, "hover_enter")
		}
		return
	}
	if !a.mouseInside {
		return
	}
	a.mouseInside = false
	a.schedulePassiveMode()
}

func (a *App) schedulePassiveMode() {
	if a == nil || a.hwnd == 0 || a.mouseInside || a.editActive || !a.overlayActive {
		return
	}
	procSetTimer.Call(uintptr(a.hwnd), TIMER_PASSIVE, uintptr((3*time.Second)/time.Millisecond), 0)
}

func (a *App) setOverlayMode(active bool, reason string) {
	if a == nil || a.hwnd == 0 {
		return
	}
	if active {
		a.overlayActive = true
		a.resizeForMode(a.activeBounds(), "active", reason)
	} else {
		a.overlayActive = false
		a.settingsOpen = false
		a.dropdown = ""
		a.resizeForMode(a.passiveBounds(), "passive", reason)
	}
	applyLayeredWindowMode()
	invalidate()
}

func (a *App) activeBounds() RECT {
	return RECT{
		Left:   a.state.Settings.X,
		Top:    a.state.Settings.Y,
		Right:  a.state.Settings.X + a.state.Settings.W,
		Bottom: a.state.Settings.Y + a.state.Settings.H,
	}
}

func (a *App) passiveBounds() RECT {
	tasks := a.visibleTasks(false)
	padding := a.scale(8)
	rowH := a.scale(max(28, a.state.Settings.FontSize+10))
	gap := a.scale(4)
	height := padding * 2
	if len(tasks) > 0 {
		height += int32(len(tasks))*rowH + int32(len(tasks)-1)*gap
	} else {
		height += rowH
	}

	maxRunes := 8
	hasChild := false
	for _, task := range tasks {
		if n := len([]rune(task.Text)); n > maxRunes {
			maxRunes = n
		}
		if task.ParentID != 0 {
			hasChild = true
		}
	}
	textWidth := int32(float64(a.font(a.state.Settings.FontSize)) * 0.58 * float64(maxRunes))
	width := padding*2 + a.scale(22) + textWidth
	if hasChild {
		width += a.scale(24)
	}
	width = clampInt32(width, a.scale(120), a.state.Settings.W)
	height = clampInt32(height, a.scale(40), a.state.Settings.H)
	return RECT{
		Left:   a.state.Settings.X,
		Top:    a.state.Settings.Y,
		Right:  a.state.Settings.X + width,
		Bottom: a.state.Settings.Y + height,
	}
}

func (a *App) resizeForMode(bounds RECT, mode, reason string) {
	width := bounds.Right - bounds.Left
	height := bounds.Bottom - bounds.Top
	a.modeChanging = true
	procSetWindowPos.Call(
		uintptr(a.hwnd),
		0,
		uintptr(bounds.Left),
		uintptr(bounds.Top),
		uintptr(width),
		uintptr(height),
		SWP_NOZORDER|SWP_NOACTIVATE,
	)
	a.modeChanging = false
	logf(
		"overlay mode=%s reason=%s bounds=%d,%d,%d,%d size=%dx%d global_window_alpha=false",
		mode,
		reason,
		bounds.Left,
		bounds.Top,
		bounds.Right,
		bounds.Bottom,
		width,
		height,
	)
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
