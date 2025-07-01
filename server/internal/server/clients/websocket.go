package clients

import (
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"net/http"
	"server/internal/server"
	"server/internal/server/states"
	"server/pkg/packets"

	"github.com/gorilla/websocket"
	"google.golang.org/protobuf/proto"
)

type WebSocketClient struct {
	id							uint64
	conn						*websocket.Conn
	hub							*server.Hub
	sendChan	chan	*packets.Packet
	state						server.ClientStateHandler
	logger					*log.Logger
	dbTx 						*server.DbTx
}

func NewWebSocketClient(hub *server.Hub, writer http.ResponseWriter, request *http.Request) (server.ClientInterfacer, error) {
	upgrader := websocket.Upgrader{
		ReadBufferSize: 	1024,
		WriteBufferSize: 	1024,
		CheckOrigin: 			func(_ *http.Request) bool { return true },
	}

	conn, err := upgrader.Upgrade(writer, request, nil)

	if err != nil {
		return nil, err
	}

	c := &WebSocketClient{
		hub: 				hub,
		conn: 			conn,
		sendChan: 	make(chan *packets.Packet, 256),
		logger: 		log.New(log.Writer(), "Client unknown: ", log.LstdFlags),
		dbTx: 			hub.NewDbTx(),
	}

	return c, nil
}

func (c *WebSocketClient) Id() uint64 {
	return c.id
}

func (c *WebSocketClient) SetState(newState server.ClientStateHandler) {
	prevStateName := "None"
	if c.state != nil {
		prevStateName = c.state.Name()
		c.state.OnExit()
	}

	newStateName := "None"
	if newState != nil {
		newStateName = newState.Name()
	}

	c.logger.Printf("Changing state from %s to %s", prevStateName, newStateName)

	c.state = newState

	if c.state != nil {
		c.state.SetClient(c)
		c.state.OnEnter()
	}
}

func (c *WebSocketClient) ProcessMessage(senderId uint64, message packets.Msg) {
	c.state.HandleMessage(senderId, message)
}

func (c *WebSocketClient) Initialize(id uint64) {
	c.id = id
	c.logger.SetPrefix((fmt.Sprintf("Client %d: ", c.id)))
	c.SetState(&states.Connected{})
}

func (c *WebSocketClient) SocketSend(message packets.Msg) {
	c.SocketSendAs(message, c.id)
}

func (c *WebSocketClient) SocketSendAs(message packets.Msg, senderId uint64) {
	select {
	case c.sendChan <- &packets.Packet{SenderId: senderId, Msg: message}:
	default:
		c.logger.Printf("Send channel full, dropping message: %T", message)
	}
}

func (c *WebSocketClient) PassToPeer(message packets.Msg, peerId uint64) {
	if peer, exists := c.hub.Clients.Get(peerId); exists {
		peer.ProcessMessage(c.id, message)
	}
}

func (c *WebSocketClient) Broadcast(message packets.Msg) {
	c.hub.BroadcastChan <- &packets.Packet{SenderId: c.id, Msg: message}
}

func (c *WebSocketClient) ReadPump() {
	defer func ()  {
		c.logger.Println("Closing read pump")
		c.Close("read pump closed")
	} ()

	for {
		_, data, err := c.conn.ReadMessage()
		if err != nil {
			if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
				c.logger.Printf("Error: %v", err)
			}
			break;
		}

		packet := &packets.Packet{}
		err = proto.Unmarshal(data, packet)
		if err != nil {
			c.logger.Printf("Error unmarshalling data: %v", err)
			continue
		}

		if (packet.SenderId == 0) {
			packet.SenderId = c.id
		}

		c.ProcessMessage(packet.SenderId, packet.Msg)
	}

}

func (c *WebSocketClient) WritePump() {
	defer func ()  {
		c.logger.Println("Closing read pump")
		c.Close("read pump closed")
	} ()

	for packet := range c.sendChan {
		writer, err := c.conn.NextWriter(websocket.BinaryMessage)
		if err != nil {
			c.logger.Printf("error getting writer for %T packet, closing client %v", packet.Msg, err)
			return
		}

		err = WriteDelimited(writer, packet)

		if err != nil {
			c.logger.Printf("error writing delimited packet: %v", err)
			continue
		}

		// data, err := proto.Marshal(packet);
		// if err != nil {
		// 	c.logger.Printf("error marshalling %T packet, closing client %v", packet.Msg, err)
		// 	continue
		// }

		// _, er := writer.Write(data)
		// if er != nil {
		// 	c.logger.Printf("error writing %T packet: %v", packet.Msg, er)
		// 	continue
		// }

		// writer.Write([]byte{'\n'})

		if err = writer.Close(); err != nil {
			c.logger.Printf("error closing writer for %T packet: %v", packet.Msg, err)
			continue
		}
	}
}

func (c *WebSocketClient) DbTx() *server.DbTx {
	return c.dbTx
}

func (c *WebSocketClient) SharedGameObjects() *server.SharedGameObjects {
	return c.hub.SharedGameObjects
}

func (c *WebSocketClient) Close(reason string) {
	c.logger.Printf("Closing client connection because: %s", reason)

	c.Broadcast(packets.NewDisconnect(reason))

	c.SetState(nil)

	c.hub.UnregisterChan <- c
	c.conn.Close()
	
	if _, closed := <-c.sendChan; !closed {
		close(c.sendChan)
	}
	// c.state.OnExit()
}

func WriteDelimited(w io.Writer, msg proto.Message) error {
	data, err := proto.Marshal(msg)
	if err != nil {
		return err
	}

	var buf [binary.MaxVarintLen64]byte
	n := binary.PutUvarint(buf[:], uint64(len(data)))

	if _, err := w.Write(buf[:n]); err != nil {
		return err
	}

	_, err = w.Write(data)
	return err
}
