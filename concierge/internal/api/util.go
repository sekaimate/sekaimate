package api

import (
	"errors"
	"os"
)

// removeIfExists deletes path, treating "already gone" as success —
// matching the C# broker's `if (File.Exists(path)) File.Delete(path)`
// pattern used throughout Program.cs.
func removeIfExists(path string) error {
	err := os.Remove(path)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	return err
}
