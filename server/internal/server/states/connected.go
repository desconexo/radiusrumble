package states

import (
	"context"
	"errors"
	"fmt"
	"log"
	"server/internal/server"
	"server/internal/server/db"
	"server/internal/server/objects"
	"server/pkg/packets"
	"strings"

	"golang.org/x/crypto/bcrypt"
)

type Connected struct {
	client  server.ClientInterfacer
	logger  *log.Logger
	queries *db.Queries
	dbCtx   context.Context
}

func (c *Connected) Name() string {
	return "Connected"
}

func (c *Connected) SetClient(client server.ClientInterfacer) {
	c.client = client
	loggingPrefix := fmt.Sprintf("Client %d [%s]: ", client.Id(), c.Name())
	c.logger = log.New(log.Writer(), loggingPrefix, log.LstdFlags)
	c.queries = client.DbTx().Queries
	c.dbCtx = client.DbTx().Ctx
}

func (c *Connected) OnEnter() {
	c.client.SocketSend(packets.NewId(c.client.Id()))
}

func (c *Connected) HandleMessage(senderId uint64, msg packets.Msg) {
	switch message := msg.(type) {
	case *packets.Packet_LoginRequest:
		c.handleLoginRequest(senderId, message)
	case *packets.Packet_RegisterRequest:
		c.handleRegisterRequest(senderId, message)
	case *packets.Packet_HiscoreBoardRequest:
		c.handleHiscoreBoardRequest(senderId, message)
		// case *packets.
	}
	// if senderId == c.client.Id() {
	// 	c.client.Broadcast(msg);
	// } else {
	// 	c.client.SocketSendAs(msg, senderId)
	// }
}

func (c *Connected) OnExit() {
}

func (c *Connected) handleLoginRequest(senderId uint64, msg *packets.Packet_LoginRequest) {
	if senderId != c.client.Id() {
		c.logger.Printf("Invalid sender ID: %d, expected: %d", senderId, c.client.Id())
		return
	}

	username := msg.LoginRequest.Username

	genericFailMessage := packets.NewDenyResponse("Incorrect username or password")

	user, err := c.queries.GetUserByUsername(c.dbCtx, strings.ToLower(username))
	if err != nil {
		c.logger.Printf("Failed to get user by username '%s': %v", username, err)
		c.client.SocketSend(genericFailMessage)
		return
	}

	err = bcrypt.CompareHashAndPassword([]byte(user.PasswordHash), []byte(msg.LoginRequest.Password))
	if err != nil {
		c.logger.Printf("Password mismatch for user '%s': %v", username, err)
		c.client.SocketSend(genericFailMessage)
		return
	}

	player, err := c.queries.GetPlayerByUserID(c.dbCtx, user.ID)

	if err != nil {
		c.logger.Printf("Error getting player for user %s: %v", username, err)
		c.client.SocketSend(genericFailMessage)
		return
	}

	c.logger.Printf("User '%s' logged in successfully", username)
	c.client.SocketSend(packets.NewOkResponse())

	c.client.SetState(&InGame{
		player: &objects.Player{
			Name:      	username,
			BestScore: 	player.BestScore,
			DbId:      	player.ID,
			Color:			uint32(player.Color),
		},
	})
}

func (c *Connected) handleRegisterRequest(senderId uint64, msg *packets.Packet_RegisterRequest) {
	if senderId != c.client.Id() {
		c.logger.Printf("Invalid sender ID: %d, expected: %d", senderId, c.client.Id())
		return
	}

	username := msg.RegisterRequest.Username

	err := validateUsername(username)
	if err != nil {
		c.logger.Printf("Invalid username '%s': %v", username, err)
		c.client.SocketSend(packets.NewDenyResponse(err.Error()))
		return
	}

	_, err = c.queries.GetUserByUsername(c.dbCtx, strings.ToLower(username))
	if err == nil {
		c.logger.Printf("Username '%s' already exists", username)
		c.client.SocketSend(packets.NewDenyResponse("Username already exists"))
		return
	}

	genericFailMessage := packets.NewDenyResponse("Failed to register user")

	passwordHash, err := bcrypt.GenerateFromPassword([]byte(msg.RegisterRequest.Password), bcrypt.DefaultCost)
	if err != nil {
		c.logger.Printf("Failed to hash password for user '%s': %v", username, err)
		c.client.SocketSend(genericFailMessage)
		return
	}

	user, err := c.queries.CreateUser(c.dbCtx, db.CreateUserParams{
		Username:     strings.ToLower(username),
		PasswordHash: string(passwordHash),
	})
	if err != nil {
		c.logger.Printf("Failed to create user '%s': %v", username, err)
		c.client.SocketSend(genericFailMessage)
		return
	}

	_, err = c.queries.CreatePlayer(c.dbCtx, db.CreatePlayerParams{
		UserID: user.ID,
		Name:   user.Username,
		Color:	int64(msg.RegisterRequest.Color),
	})

	if err != nil {
		c.logger.Printf("Failed to create player for user %s: %v", username, err)
		c.client.SocketSend(genericFailMessage)
		return
	}

	c.logger.Printf("User '%s' registered successfully", username)
	c.client.SocketSend(packets.NewOkResponse())
}

func (c *Connected) handleHiscoreBoardRequest(senderId uint64, message *packets.Packet_HiscoreBoardRequest) {
	c.client.SetState(&BrowsingHiscores{})
}

func validateUsername(username string) error {
	if len(username) < 3 || len(username) > 20 {
		return errors.New("username must be between 3 and 20 characters")
	}
	if username != strings.TrimSpace(username) {
		return errors.New("username cannot have leading or trailing spaces")
	}
	return nil
}
