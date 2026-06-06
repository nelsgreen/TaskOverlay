//go:build windows

package main

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
