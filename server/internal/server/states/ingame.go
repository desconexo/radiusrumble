package states

import (
	"context"
	"fmt"
	"log"
	"math"
	"math/rand/v2"
	"server/internal/server"
	"server/internal/server/db"
	"server/internal/server/objects"
	"server/pkg/packets"
	"time"
)

type InGame struct {
	client                 server.ClientInterfacer
	player                 *objects.Player
	logger                 *log.Logger
	cancelPlayerUpdateLoop context.CancelFunc
}

func (g *InGame) Name() string {
	return "InGame"
}

func (g *InGame) SetClient(client server.ClientInterfacer) {
	g.client = client
	loggingPrefix := fmt.Sprintf("Client %d [%s]: ", client.Id(), g.Name())
	g.logger = log.New(log.Writer(), loggingPrefix, log.LstdFlags)
}

func (g *InGame) OnEnter() {
	g.logger.Printf("Adding player %s to the shared collection", g.player.Name)
	go g.client.SharedGameObjects().Players.Add(g.player, g.client.Id())

	// Set initial properties for the player
	// g.player.X = rand.Float64() * 1000
	// g.player.Y = rand.Float64() * 1000
	g.player.X, g.player.Y = objects.SpawnCoords(g.player.Radius, g.client.SharedGameObjects().Players, nil)
	g.player.Speed = 150.0
	g.player.Radius = 20.0

	g.client.SocketSend(packets.NewPlayer(g.client.Id(), g.player))

	go g.sendInitialSpores(20, 50)
}

func (g *InGame) HandleMessage(senderId uint64, msg packets.Msg) {
	switch msg := msg.(type) {
	case *packets.Packet_Player:
		g.handlePlayer(senderId, msg)
	case *packets.Packet_PlayerDirection:
		g.handlePlayerDirection(senderId, msg)
	case *packets.Packet_Chat:
		g.handleChat(senderId, msg)
	case *packets.Packet_SporeConsumed:
		g.handleSporeConsumed(senderId, msg)
	case *packets.Packet_PlayerConsumed:
		g.handlePlayerConsumed(senderId, msg)
	case *packets.Packet_Spore:
		g.handleSpore(senderId, msg)
	case *packets.Packet_Disconnect:
		g.handleDisconnect(senderId, msg)
	}
}

func (g *InGame) OnExit() {
	g.logger.Printf("Removing player %s from the shared collection", g.player.Name)
	if g.cancelPlayerUpdateLoop != nil {
		g.cancelPlayerUpdateLoop()
	}
	g.syncPlayerBestScore()
	go g.client.SharedGameObjects().Players.Remove(g.client.Id())
}

func (g *InGame) handlePlayer(senderId uint64, message *packets.Packet_Player) {
	if senderId == g.client.Id() {
		g.logger.Printf("Received player message from our own client, ignoring")
		return
	}

	g.client.SocketSendAs(message, senderId)
}

func (g *InGame) handleChat(senderId uint64, msg *packets.Packet_Chat) {
	if senderId == g.client.Id() {
		g.client.Broadcast(msg)
	} else {
		g.client.SocketSendAs(msg, senderId)
	}
}

func (g *InGame) handlePlayerDirection(senderId uint64, msg *packets.Packet_PlayerDirection) {
	if senderId != g.client.Id() {
		g.logger.Printf("Received player direction message from another client (ID: %d), ignoring", senderId)
		return
	}

	g.player.Direction = msg.PlayerDirection.Direction

	if g.cancelPlayerUpdateLoop == nil {
		ctx, cancel := context.WithCancel(context.Background())
		g.cancelPlayerUpdateLoop = cancel
		go g.playerUpdateLoop(ctx)
	}

}

func (g *InGame) handleSporeConsumed(senderId uint64, msg *packets.Packet_SporeConsumed) {
	if senderId != g.client.Id() {
		g.client.SocketSendAs(msg, senderId)
		return
	}

	errMsg := "Could not verify spore consumption: "

	sporeId := msg.SporeConsumed.SporeId
	spore, err := g.getSpore(sporeId)
	if err != nil {
		g.logger.Printf("%s%s", errMsg, err.Error())
		return
	}

	err = g.validatePlayerCloseToObject(spore.X, spore.Y, spore.Radius, 10.0)
	if err != nil {
		g.logger.Printf("%s%s", errMsg, err.Error())
		return
	}

	err = g.validatePlayerDropCooldown(spore, 10)
	if err != nil {
		g.logger.Printf("%s%s", errMsg, err.Error())
		return
	}

	sporeMass := radToMass(spore.Radius)
	g.player.Radius = g.nextRadius(sporeMass)

	go g.client.SharedGameObjects().Spores.Remove(sporeId)

	g.client.Broadcast(msg)

	go g.syncPlayerBestScore()
}

func (g *InGame) handlePlayerConsumed(senderId uint64, msg *packets.Packet_PlayerConsumed) {
	if senderId != g.client.Id() {
		g.client.SocketSendAs(msg, senderId)

		if msg.PlayerConsumed.PlayerId == g.client.Id() {
			g.logger.Println("Player was consumed, respawning...")
			g.client.SetState(&InGame{
				player: &objects.Player{
					Name: g.player.Name,
				},
			})
		}

		return
	}

	errMsg := "Could not verify player consumption: "

	otherId := msg.PlayerConsumed.PlayerId
	other, err := g.getOtherPlayer(otherId)
	if err != nil {
		g.logger.Printf("%s%s", errMsg, err.Error())
		return
	}

	ourMass := radToMass(g.player.Radius)
	otherMass := radToMass(other.Radius)
	if ourMass <= otherMass*1.5 {
		g.logger.Println(errMsg + "player's mass not enough")
		return
	}

	err = g.validatePlayerCloseToObject(other.X, other.Y, other.Radius, 10.0)
	if err != nil {
		g.logger.Printf("%s%s", errMsg, err.Error())
		return
	}

	g.player.Radius = g.nextRadius(otherMass)

	go g.client.SharedGameObjects().Players.Remove(otherId)
	g.client.Broadcast(msg)

	go g.syncPlayerBestScore()

}

func (g *InGame) handleSpore(senderId uint64, msg *packets.Packet_Spore) {
	g.client.SocketSendAs(msg, senderId)
}

func (g *InGame) handleDisconnect(senderId uint64, msg *packets.Packet_Disconnect) {
	if senderId == g.client.Id() {
		g.client.Broadcast(msg)
		g.client.SetState(&Connected{})
		return
	}

	go g.client.SocketSendAs(msg, senderId)
}

func (g *InGame) playerUpdateLoop(ctx context.Context) {
	const delta float64 = 0.05
	ticker := time.NewTicker(time.Duration(delta*1000) * time.Millisecond)
	defer ticker.Stop()

	for {
		select {
		case <-ticker.C:
			g.syncPlayer(delta)
		case <-ctx.Done():
			return
		}
	}
}

func (g *InGame) syncPlayer(delta float64) {
	newX := g.player.X + g.player.Speed*math.Cos(g.player.Direction)*delta
	newY := g.player.Y + g.player.Speed*math.Sin(g.player.Direction)*delta

	g.player.X = newX
	g.player.Y = newY

	probability := g.player.Radius / float64(server.MaxSpores*5)
	if rand.Float64() < probability && g.player.Radius > 10 {
		spore := &objects.Spore{
			X:         g.player.X,
			Y:         g.player.Y,
			Radius:    min(5+g.player.Radius/50, 15),
			DroppedBy: g.player,
			DroppedAt: time.Now(),
		}
		sporeId := g.client.SharedGameObjects().Spores.Add(spore)
		g.client.Broadcast(packets.NewSpore(sporeId, spore))
		go g.client.SocketSend(packets.NewSpore(sporeId, spore))
		g.player.Radius = g.nextRadius(-radToMass(spore.Radius))
	}

	updatePacket := packets.NewPlayer(g.client.Id(), g.player)
	g.client.Broadcast(updatePacket)
	go g.client.SocketSend(updatePacket)
}

func (g *InGame) sendInitialSpores(batchSize int, delay time.Duration) {
	sporesBatch := make(map[uint64]*objects.Spore, batchSize)

	g.client.SharedGameObjects().Spores.ForEach(func(sporeId uint64, spore *objects.Spore) {
		sporesBatch[sporeId] = spore

		if len(sporesBatch) >= batchSize {
			g.client.SocketSend(packets.NewSporesBatch(sporesBatch))
			sporesBatch = make(map[uint64]*objects.Spore, batchSize)
			time.Sleep(delay * time.Millisecond)
		}
	})

	// Send any remaining spores in the batch
	if len(sporesBatch) > 0 {
		g.client.SocketSend(packets.NewSporesBatch(sporesBatch))
	}
}

func (g *InGame) getSpore(sporeId uint64) (*objects.Spore, error) {
	spore, exists := g.client.SharedGameObjects().Spores.Get(sporeId)
	if !exists {
		return nil, fmt.Errorf("spore with ID %d not found", sporeId)
	}
	return spore, nil
}

func (g *InGame) getOtherPlayer(playerId uint64) (*objects.Player, error) {
	player, exists := g.client.SharedGameObjects().Players.Get(playerId)
	if !exists {
		return nil, fmt.Errorf("player with ID %d not found", playerId)
	}
	return player, nil
}

func (g *InGame) validatePlayerCloseToObject(objX, objY, objRadius, buffer float64) error {
	realDX := g.player.X - objX
	realDY := g.player.Y - objY
	realDistanceSq := realDX*realDX + realDY*realDY

	thresholdDistance := g.player.Radius + buffer + objRadius
	thresholdDistanceSq := thresholdDistance * thresholdDistance

	if realDistanceSq > thresholdDistanceSq {
		return fmt.Errorf("player is too far from the object (distance: %f, threshold: %f)", realDistanceSq, thresholdDistance)
	}

	return nil
}

func massToRad(mass float64) float64 {
	return math.Sqrt(mass / math.Pi)
}

func radToMass(radius float64) float64 {
	return math.Pi * radius * radius
}

func (g *InGame) nextRadius(massDiff float64) float64 {
	oldMass := radToMass(g.player.Radius)
	newMass := oldMass + massDiff
	return massToRad(newMass)
}

func (g *InGame) syncPlayerBestScore() {
	currentScore := int64(math.Floor(radToMass(g.player.Radius)))
	if currentScore > g.player.BestScore {
		g.player.BestScore = currentScore
		err := g.client.DbTx().Queries.UpdatePlayerBestScore(g.client.DbTx().Ctx, db.UpdatePlayerBestScoreParams{
			ID:        g.player.DbId,
			BestScore: g.player.BestScore,
		})
		if err != nil {
			g.logger.Printf("Error updating player best score: %v", err)
		}
	}
}

func (g *InGame) validatePlayerDropCooldown(spore *objects.Spore, buffer float64) error {
	minAcceptableDistance := spore.Radius + g.player.Radius + buffer
	minAcceptableTime := time.Duration(minAcceptableDistance/g.player.Speed*1000) * time.Millisecond
	if spore.DroppedBy == g.player && time.Since(spore.DroppedAt) < minAcceptableTime {
		return fmt.Errorf("player dropped the spore too recently (time %v, min acceptable time: %v)", time.Since(spore.DroppedAt), minAcceptableTime)
	}

	return nil
}
