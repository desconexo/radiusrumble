package objects

import "math/rand/v2"

func SpawnCoords(radius float64, playersToAvoid *SharedCollection[*Player], sporesToAvoid *SharedCollection[*Spore]) (float64, float64) {
	bound := 3000.0

	const maxTries = 25

	tries := 0

	for {
		x := bound * (2 * rand.Float64() - 1)
		y := bound * (2 * rand.Float64() - 1)

		if !isTooClose(x, y, radius, playersToAvoid, getPlayerPosition, getPlayerRadius) && !isTooClose(x, y, radius, sporesToAvoid, getSporePosition, getSporeRadius) {
			return x, y
		}

		tries++
		if tries >= maxTries {
			bound *= 2
			tries = 0
		}
	}
}

var getPlayerPosition = func(p *Player) (float64, float64) {
	return p.X, p.Y
}

var getPlayerRadius = func(p *Player) float64 {
	return p.Radius
}

var getSporePosition = func(s *Spore) (float64, float64) {
	return s.X, s.Y
}

var getSporeRadius = func(s *Spore) float64 {
	return s.Radius
}

func isTooClose[T any](x, y, radius float64, objects *SharedCollection[T], getPosition func(T) (float64, float64), getRadius func(T) float64) bool {
	if objects == nil {
		return false
	}

	tooClose := false
	objects.ForEach(func(_ uint64, obj T) {
		if tooClose {
			return
		}

		objX, objY := getPosition(obj)
		objRadius := getRadius(obj)
		xDist := objX - x
		yDist := objY - y
		dstSq := xDist*xDist + yDist*yDist

		if dstSq < (radius+objRadius)*(radius+objRadius) {
			tooClose = true
			return
		}
	})

	return tooClose
}