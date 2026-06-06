//go:build windows

package main

import (
	"math"
	"strconv"
	"unsafe"
)

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
