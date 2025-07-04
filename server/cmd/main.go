package main

import (
	"flag"
	"fmt"
	"log"
	"net/http"
	"os"
	"server/internal/server"
	"server/internal/server/clients"
	"strconv"

	"github.com/joho/godotenv"
)

const (
	dockerMountedDataDir = "/gameserver/data"
)

type config struct {
	Port int
	DataPath string
}

var (
	defaultConfig = &config{ Port: 8080 }
	configPath = flag.String("config", ".env", "Path to the config file")
)

func loadConfig() *config {
	cfg := defaultConfig
	cfg.DataPath = os.Getenv("DATA_PATH")

	port, err := strconv.Atoi(os.Getenv("PORT"))
	if err != nil {
		log.Printf("Error parsing PORT, using %d", cfg.Port)
		return cfg
	}

	cfg.Port = port
	return cfg
}

func coalescePaths(fallbacks ...string) string {
	for i, path := range fallbacks {
		if _, err := os.Stat(path); os.IsNotExist(err) {
			message := fmt.Sprintf("File/folder not found at %s", path)
			if i < len(fallbacks) - 1 {
				log.Printf("%s - going to try %s", message, fallbacks[i+1])
			} else {
				log.Printf("%s - no more fallbacks to try", message)
			}
		} else {
			log.Printf("File/folder found at %s", path)
			return path
		}
	}

	return ""
}

func main() {
	flag.Parse()
	err := godotenv.Load(*configPath)
	cfg := defaultConfig
	if err != nil {
		log.Printf("Error loading config file, defaulting to %v", defaultConfig)
	} else {
		cfg = loadConfig()
	}

	cfg.DataPath = coalescePaths(cfg.DataPath, dockerMountedDataDir, ".")

	hub := server.NewHub(cfg.DataPath)

	http.HandleFunc("/ws", func(w http.ResponseWriter, r *http.Request) {
		hub.Serve(clients.NewWebSocketClient, w, r)
	})

	go hub.Run()
	addr := fmt.Sprintf(":%d", cfg.Port)
	log.Printf("Starting server on %s", addr)
	err = http.ListenAndServe(addr, nil)

	if err != nil {
		log.Fatalf("ListenAndServer: %v", err)
	}
}