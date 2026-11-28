/** CR-065: the imported RCS map. Positions are the numbers of the file; the settings say how they relate to the RCS posX / posY (millimetres). */
export interface MapPoint { id: number; x: number; y: number; value: string | null; storageType: string | null }
export interface MapEdge { from: number; to: number; distance: number | null; ctrl: number[][] }
export interface RcsMapInfo {
  mapId: string; mapName: string; mapQrCode: string | null; mapType: string | null; row: string | null; col: string | null; width: string | null; height: string | null
  fileName: string; sha256: string; pointCount: number; edgeCount: number; importedAt: string; importedBy: string | null
  mmPerUnit: number; yAxisUp: boolean; headingOffsetDeg: number; headingClockwise: boolean; rcsMapCodes: string[]; confirmedAt: string | null; confirmedBy: string | null
}
export interface RcsMapView { info: RcsMapInfo; geometry: { points: MapPoint[]; edges: MapEdge[] } }
export interface RcsMapImportResult { info: RcsMapInfo; replaced: boolean; unknownNeighbours: number }
export interface RcsMapSettings { mmPerUnit: number; yAxisUp: boolean; headingOffsetDeg: number; headingClockwise: boolean; rcsMapCodes: string[]; confirmed: boolean }
