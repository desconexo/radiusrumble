package server

import (
	"context"
	"database/sql"
	_ "embed"
	"log"
	"math/rand/v2"
	"net/http"
	"path"
	"server/internal/server/db"
	"server/internal/server/objects"
	"server/pkg/packets"
	"time"

	_ "modernc.org/sqlite"
)

const MaxSpores = 1000

//go:embed db/config/schema.sql
var schemaGenSql string

type DbTx struct {
	Ctx context.Context
	Queries *db.Queries
}

func (h *Hub) NewDbTx() *DbTx {
	return &DbTx{
		Ctx: context.Background(),
		Queries: db.New(h.dbPool),
	}
}

type SharedGameObjects struct {
	// The ID of the player is the ID of the client
	Players *objects.SharedCollection[*objects.Player]
	Spores 	*objects.SharedCollection[*objects.Spore]
}

type ClientStateHandler interface {
	Name() string

	// Inject the client into the state handler
	SetClient(client ClientInterfacer)

	OnEnter()

	HandleMessage(senderId uint64, Msg packets.Msg)

	OnExit() 
}

type ClientInterfacer interface {
	Id() uint64

	ProcessMessage(senderId uint64, Msg packets.Msg)

	Initialize(id uint64)

	SetState(newState ClientStateHandler)

	SocketSend(message packets.Msg)

	SocketSendAs(message packets.Msg, senderId uint64)

	PassToPeer(message packets.Msg, peerId uint64)

	Broadcast(message packets.Msg)

	ReadPump()

	WritePump()

	DbTx() *DbTx

	SharedGameObjects() *SharedGameObjects

	Close(reason string)
}

type Hub struct {
	Clients *objects.SharedCollection[ClientInterfacer]

	BroadcastChan chan *packets.Packet

	RegisterChan chan ClientInterfacer

	UnregisterChan chan ClientInterfacer

	dbPool *sql.DB

	SharedGameObjects *SharedGameObjects
}

func NewHub(dataDirPath string) *Hub {
	dbPool, err := sql.Open("sqlite", path.Join(dataDirPath, "db.sqlite"))
	if err != nil {
		log.Fatalf("Failed to open database: %v", err)
	}

	return &Hub{
		Clients:				objects.NewSharedCollection[ClientInterfacer](),
		BroadcastChan:	make(chan *packets.Packet),
		RegisterChan:		make(chan ClientInterfacer),
		UnregisterChan:	make(chan ClientInterfacer),
		dbPool:					dbPool,
		SharedGameObjects: &SharedGameObjects{
			Players: objects.NewSharedCollection[*objects.Player](),
			Spores:  objects.NewSharedCollection[*objects.Spore](),
		},
	}
}

func (h *Hub) Run() {
	log.Println("Initializing database schema")
	if _, err := h.dbPool.ExecContext(context.Background(), schemaGenSql); err != nil {
		log.Fatalf("Failed to initialize database schema: %v", err)
	}

	log.Println("Placing spores...")
	for i := 0; i < MaxSpores; i++ {
		h.SharedGameObjects.Spores.Add(h.newSpore())
	}

	go h.repenishSporesLoop(5)

	log.Println("Awaiting client registrations")
	for {
		select {
		case client := <-h.RegisterChan:
			client.Initialize(h.Clients.Add(client))
		case client := <-h.UnregisterChan:
			h.Clients.Remove(client.Id())
		case packet := <-h.BroadcastChan:
			// for _, client := range h.Clients {
			h.Clients.ForEach(func(id uint64, client ClientInterfacer) {
				if id != packet.SenderId {
					client.ProcessMessage(packet.SenderId, packet.Msg)
				}
			})
		}
	}
}

func (h *Hub) Serve(getNewClient func(*Hub, http.ResponseWriter, *http.Request) (ClientInterfacer, error), writer http.ResponseWriter, request *http.Request) {
	log.Println("New client connected from", request.RemoteAddr)
	client, err := getNewClient(h, writer, request)

	if err != nil {
		log.Printf("Error obtaining client for new connection: %v", err)
		return
	}

	h.RegisterChan <- client

	go client.WritePump()
	go client.ReadPump()
}

func (h *Hub) newSpore() *objects.Spore {
	sporeRadius := max(10 + rand.NormFloat64() * 3, 5)
	x, y := objects.SpawnCoords(sporeRadius, h.SharedGameObjects.Players, h.SharedGameObjects.Spores)
	return &objects.Spore{
		X:      x,
		Y:      y,
		Radius: sporeRadius,
	}
}

func (h *Hub) repenishSporesLoop(rate time.Duration) {
	ticker := time.NewTicker(rate * time.Second)
	defer ticker.Stop()

	for range ticker.C {
		sporesRemaining := h.SharedGameObjects.Spores.Len()
		diff := MaxSpores - sporesRemaining
		if diff <= 0 {
			continue
		}

		log.Printf("Replenishing %d spores", diff)
		for i := 0; i < min(diff, 10); i++ {
			spore := h.newSpore()
			sporeId := h.SharedGameObjects.Spores.Add(spore)

			h.BroadcastChan <- &packets.Packet{
				SenderId: 0,
				Msg:			packets.NewSpore(sporeId, spore),
			}

			time.Sleep(50 * time.Millisecond)
		}
	}
}