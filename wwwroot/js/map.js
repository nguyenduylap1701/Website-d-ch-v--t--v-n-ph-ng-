/**
 * Module Bản đồ tương tác & Tính khoảng cách trường học (Leaflet & OpenStreetMap)
 * Phụ trách: Nguyễn Đình Anh (Tính năng R03 - Phân hệ Tìm kiếm nâng cao & Bản đồ)
 */

(function () {
  let mainMap = null;
  let detailMap = null;
  let roomMarkers = [];
  let landmarkMarker = null;
  let activeRouteLine = null;
  let currentLandmarks = [];
  let currentRooms = [];

  // Icon trường học / Điểm đến
  const universityIcon = L.divIcon({
    className: 'custom-map-marker landmark-marker',
    html: `<div class="marker-pin landmark-pin"><span class="marker-icon">🎓</span></div>`,
    iconSize: [40, 40],
    iconAnchor: [20, 38],
    popupAnchor: [0, -34]
  });

  // Icon phòng trọ
  function createRoomIcon(priceText, isVerified) {
    return L.divIcon({
      className: 'custom-map-marker room-marker',
      html: `<div class="marker-pin room-pin ${isVerified ? 'verified' : ''}">
               <span class="marker-label">${priceText}</span>
             </div>`,
      iconSize: [64, 34],
      iconAnchor: [32, 34],
      popupAnchor: [0, -30]
    });
  }

  // Công thức Haversine tính khoảng cách đường chim bay / xấp xỉ tuyến phố (km)
  function calculateDistance(lat1, lon1, lat2, lon2) {
    const R = 6371; // Bán kính Trái Đất theo km
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const a =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
      Math.sin(dLon / 2) * Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    // Nhân hệ số 1.25 mô phỏng đường đi thực tế qua mạng lưới phố xá Hà Nội
    return (R * c * 1.25);
  }

  function formatShortPrice(price) {
    if (price >= 1000000) {
      const millions = price / 1000000;
      return millions % 1 === 0 ? `${millions}tr` : `${millions.toFixed(1)}tr`;
    }
    return `${Math.round(price / 1000)}k`;
  }

  // Khởi tạo bản đồ trang tìm phòng
  async function initRoomsMap() {
    const mapElement = document.getElementById('roomsMap');
    if (!mapElement) return;

    if (mainMap) {
      mainMap.remove();
      mainMap = null;
    }

    // Tọa độ trung tâm Hà Nội (Khu vực Đống Đa - Ba Đình - Cầu Giấy)
    mainMap = L.map('roomsMap', {
      zoomControl: true,
      scrollWheelZoom: true
    }).setView([21.0285, 105.8242], 13);

    // Sử dụng CartoDB Voyager tile (không cần subdomain, hoạt động ổn định tại VN)
    L.tileLayer('https://basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
      attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors © <a href="https://carto.com/attributions">CARTO</a>'
    }).addTo(mainMap);

    // Tải danh sách trường đại học
    try {
      const res = await fetch('/api/landmarks');
      if (res.ok) {
        currentLandmarks = await res.json();
        populateLandmarkDropdown(currentLandmarks);
      }
    } catch (e) {
      console.error('Không thể tải landmarks:', e);
    }

    // Tải danh sách phòng trọ có tọa độ
    await loadRoomsToMap();

    // Bắt sự kiện chuyển trường đại học
    const landmarkSelect = document.getElementById('mapLandmarkSelect');
    if (landmarkSelect) {
      landmarkSelect.addEventListener('change', handleLandmarkChange);
    }

    const radiusSelect = document.getElementById('mapRadiusSelect');
    if (radiusSelect) {
      radiusSelect.addEventListener('change', handleLandmarkChange);
    }

    // Fix kích thước bản đồ khi hiển thị
    setTimeout(() => {
      mainMap.invalidateSize();
    }, 300);
  }

  function populateLandmarkDropdown(landmarks) {
    const select = document.getElementById('mapLandmarkSelect');
    if (!select) return;
    select.innerHTML = '<option value="">-- Chọn trường Đại học / Điểm đến --</option>' +
      landmarks.map(l => `<option value="${l.id}">${l.name} (${l.address})</option>`).join('');
  }

  async function loadRoomsToMap() {
    try {
      const res = await fetch('/api/rooms?limit=100');
      if (!res.ok) return;
      currentRooms = await res.json();
      renderMarkers(currentRooms);
    } catch (e) {
      console.error('Lỗi tải phòng lên bản đồ:', e);
    }
  }

  function renderMarkers(rooms, selectedLandmark = null, maxRadiusKm = null) {
    if (!mainMap) return;

    // Xóa marker cũ
    roomMarkers.forEach(m => mainMap.removeLayer(m));
    roomMarkers = [];
    if (activeRouteLine) {
      mainMap.removeLayer(activeRouteLine);
      activeRouteLine = null;
    }

    const bounds = L.latLngBounds();
    let visibleRooms = [];

    rooms.forEach(room => {
      if (!room.latitude || !room.longitude) return;

      let distanceKm = null;
      if (selectedLandmark) {
        distanceKm = calculateDistance(selectedLandmark.lat, selectedLandmark.lng, room.latitude, room.longitude);
        if (maxRadiusKm && distanceKm > maxRadiusKm) {
          return; // Vượt quá bán kính lọc
        }
      }

      visibleRooms.push({ room, distanceKm });

      const priceLabel = formatShortPrice(room.price);
      const marker = L.marker([room.latitude, room.longitude], {
        icon: createRoomIcon(priceLabel, room.verified)
      });

      // Tạo popup
      const normalDuration = distanceKm ? Math.round(distanceKm / 25 * 60) : null;
      const rushHourDuration = normalDuration ? Math.round(normalDuration * 1.45) : null;
      const walkDuration = distanceKm ? Math.round(distanceKm / 4.5 * 60) : null;

      const popupContent = `
        <div class="map-popup-card">
          <img src="${room.image}" alt="${room.title}" class="map-popup-img" />
          <div class="map-popup-body">
            <div class="map-popup-tags">
              <span class="map-popup-badge">${room.verified ? '✓ Đã xác minh' : 'Phòng trọ'}</span>
              <span class="map-popup-price">${window.formatPrice ? window.formatPrice(room.price) : room.price + ' đ'}/tháng</span>
            </div>
            <h4 class="map-popup-title"><a href="/phong/${room.id}">${room.title}</a></h4>
            <p class="map-popup-addr">📍 ${room.address || room.district}</p>
            ${distanceKm ? `
              <div class="map-popup-distance">
                <div class="dist-header">🧭 Cách <strong>${selectedLandmark.name}</strong>: <strong>${distanceKm.toFixed(1)} km</strong></div>
                <div class="dist-meta">
                  <span>🛵 Xe máy: ~${normalDuration} phút (Cao điểm: ~${rushHourDuration} phút)</span>
                  <span>🚶 Đi bộ: ~${walkDuration} phút</span>
                </div>
              </div>
            ` : ''}
            <div class="map-popup-actions">
              <a href="/phong/${room.id}" class="btn-popup-detail">Xem chi tiết</a>
              ${selectedLandmark ? `<button type="button" class="btn-popup-route" onclick="window.drawRoute(${room.latitude}, ${room.longitude}, '${encodeURIComponent(room.title)}')">Chỉ đường</button>` : ''}
            </div>
          </div>
        </div>
      `;

      marker.bindPopup(popupContent, { maxWidth: 300 });
      marker.addTo(mainMap);
      roomMarkers.push(marker);
      bounds.extend([room.latitude, room.longitude]);
    });

    if (selectedLandmark) {
      bounds.extend([selectedLandmark.lat, selectedLandmark.lng]);
    }

    if (roomMarkers.length > 0) {
      mainMap.fitBounds(bounds, { padding: [40, 40], maxZoom: 15 });
    }

    // Cập nhật số lượng phòng tìm được
    const mapCountEl = document.getElementById('mapResultCount');
    if (mapCountEl) {
      mapCountEl.textContent = `Hiển thị ${visibleRooms.length} phòng trọ trên bản đồ`;
    }
  }

  function handleLandmarkChange() {
    const select = document.getElementById('mapLandmarkSelect');
    const radiusSelect = document.getElementById('mapRadiusSelect');
    const routeBox = document.getElementById('routeSummaryBox');
    if (routeBox) routeBox.style.display = 'none';

    if (!select || !select.value) {
      if (landmarkMarker && mainMap) {
        mainMap.removeLayer(landmarkMarker);
        landmarkMarker = null;
      }
      renderMarkers(currentRooms);
      return;
    }

    const landmark = currentLandmarks.find(l => l.id === select.value);
    if (!landmark || !mainMap) return;

    // Đặt marker cho trường ĐH
    if (landmarkMarker) mainMap.removeLayer(landmarkMarker);
    landmarkMarker = L.marker([landmark.lat, landmark.lng], { icon: universityIcon })
      .bindPopup(`<strong>🎓 ${landmark.name}</strong><br/>${landmark.address}`)
      .addTo(mainMap);

    landmarkMarker.openPopup();

    const maxRadius = radiusSelect && radiusSelect.value ? parseFloat(radiusSelect.value) : null;
    renderMarkers(currentRooms, landmark, maxRadius);
  }

  // Hàm vẽ đường chỉ đường
  window.drawRoute = function (roomLat, roomLng, roomTitleEncoded) {
    if (!mainMap || !landmarkMarker) return;
    const roomTitle = decodeURIComponent(roomTitleEncoded);
    const landmarkLatLng = landmarkMarker.getLatLng();

    if (activeRouteLine) {
      mainMap.removeLayer(activeRouteLine);
    }

    // Vẽ đường polyline từ trường học đến phòng trọ
    activeRouteLine = L.polyline([
      [landmarkLatLng.lat, landmarkLatLng.lng],
      [roomLat, roomLng]
    ], {
      color: '#3b82f6',
      weight: 5,
      opacity: 0.85,
      dashArray: '8, 8'
    }).addTo(mainMap);

    mainMap.fitBounds(activeRouteLine.getBounds(), { padding: [50, 50] });

    // Tính toán lộ trình
    const distanceKm = calculateDistance(landmarkLatLng.lat, landmarkLatLng.lng, roomLat, roomLng);
    const normalMin = Math.round(distanceKm / 25 * 60);
    const rushMin = Math.round(normalMin * 1.45);
    const walkMin = Math.round(distanceKm / 4.5 * 60);

    const routeBox = document.getElementById('routeSummaryBox');
    if (routeBox) {
      routeBox.innerHTML = `
        <div class="route-summary-card">
          <div class="route-header">
            <h4>🗺️ Lộ trình từ Trường đến: <span>${roomTitle}</span></h4>
            <button type="button" class="btn-close-route" onclick="document.getElementById('routeSummaryBox').style.display='none'">✕</button>
          </div>
          <div class="route-stats">
            <div class="route-stat-item">
              <span class="stat-icon">📏</span>
              <div><small>Khoảng cách</small><strong>${distanceKm.toFixed(1)} km</strong></div>
            </div>
            <div class="route-stat-item highlight">
              <span class="stat-icon">🛵</span>
              <div><small>Giờ thường</small><strong>~${normalMin} phút</strong></div>
            </div>
            <div class="route-stat-item warning">
              <span class="stat-icon">⚠️</span>
              <div><small>Giờ cao điểm</small><strong>~${rushMin} phút</strong></div>
            </div>
            <div class="route-stat-item">
              <span class="stat-icon">🚶</span>
              <div><small>Đi bộ</small><strong>~${walkMin} phút</strong></div>
            </div>
          </div>
        </div>
      `;
      routeBox.style.display = 'block';
    }
  };

  // Khởi tạo bản đồ trang Chi tiết phòng (RoomDetail)
  async function initDetailRoomMap() {
    const detailMapEl = document.getElementById('detailRoomMap');
    if (!detailMapEl) return;

    const lat = parseFloat(detailMapEl.dataset.lat || '21.0285');
    const lng = parseFloat(detailMapEl.dataset.lng || '105.8242');
    const roomTitle = detailMapEl.dataset.title || 'Phòng trọ';
    const roomAddress = detailMapEl.dataset.address || '';

    if (detailMap) {
      detailMap.remove();
      detailMap = null;
    }

    detailMap = L.map('detailRoomMap', {
      zoomControl: true,
      scrollWheelZoom: false
    }).setView([lat, lng], 14);

    L.tileLayer('https://basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
      attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors © <a href="https://carto.com/attributions">CARTO</a>'
    }).addTo(detailMap);

    const roomPin = L.marker([lat, lng], {
      icon: L.divIcon({
        className: 'custom-map-marker room-marker',
        html: `<div class="marker-pin room-pin verified"><span class="marker-label">🏠 Vị trí phòng</span></div>`,
        iconSize: [100, 34],
        iconAnchor: [50, 34]
      })
    }).addTo(detailMap);

    roomPin.bindPopup(`<strong>${roomTitle}</strong><br/>${roomAddress}`).openPopup();

    // Tải các trường ĐH gần nhất
    try {
      const res = await fetch('/api/landmarks');
      if (res.ok) {
        const landmarks = await res.json();
        const nearbyTableBody = document.getElementById('nearbyUniversitiesBody');
        if (nearbyTableBody) {
          const distances = landmarks.map(l => {
            const dist = calculateDistance(lat, lng, l.lat, l.lng);
            const normalMin = Math.round(dist / 25 * 60);
            return { ...l, dist, normalMin };
          }).sort((a, b) => a.dist - b.dist);

          nearbyTableBody.innerHTML = distances.slice(0, 5).map(item => `
            <tr>
              <td><strong>${item.name}</strong><br/><small class="text-muted">${item.address}</small></td>
              <td><span class="badge-km">${item.dist.toFixed(1)} km</span></td>
              <td>🛵 ~${item.normalMin} phút</td>
            </tr>
          `).join('');

          // Ghim các trường ĐH lân cận lên bản đồ mini
          distances.slice(0, 3).forEach(l => {
            L.marker([l.lat, l.lng], { icon: universityIcon })
              .bindPopup(`<strong>🎓 ${l.name}</strong><br/>Cách phòng ~${l.dist.toFixed(1)} km`)
              .addTo(detailMap);
          });
        }
      }
    } catch (e) {
      console.error('Lỗi tính khoảng cách trường đại học:', e);
    }

    setTimeout(() => {
      detailMap.invalidateSize();
    }, 300);
  }

  // Khởi tạo toggle xem bản đồ trên trang Rooms
  function setupViewModeSwitch() {
    const switchButtons = document.querySelectorAll('[data-view-mode]');
    const mapWrapper = document.getElementById('roomsMapWrapper');
    const roomsGridList = document.querySelector('.rooms-grid-list');

    switchButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const mode = btn.dataset.viewMode;
        if (mode === 'map') {
          if (mapWrapper) mapWrapper.style.display = 'block';
          if (roomsGridList) roomsGridList.style.display = 'none';
          if (mainMap) {
            setTimeout(() => mainMap.invalidateSize(), 200);
          } else {
            initRoomsMap();
          }
        } else {
          if (mapWrapper) mapWrapper.style.display = 'none';
          if (roomsGridList) roomsGridList.style.display = 'grid';
        }
      });
    });
  }

  // Khởi động khi tải trang hoặc chuyển trang trong SPA
  function initializeMapModule() {
    initRoomsMap();
    initDetailRoomMap();
    setupViewModeSwitch();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeMapModule);
  } else {
    initializeMapModule();
  }
  document.addEventListener('troto:page-loaded', initializeMapModule);
})();
