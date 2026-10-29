window.formatPrice = function (value) {
  return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 }).format(value);
};

window.initializeTroTotPage = async () => {
  const data = window.TroTotData || { districts: [], testimonials: [], blogPosts: [], faqs: [] };
  const filterForm = document.querySelector('.filter-form');
  const quickFilters = document.querySelector('.rooms-quick-filters');
  if (filterForm && quickFilters) {
    const syncQuickValue = (field) => {
      const source = filterForm.querySelector(`[data-filter-field="${field}"]`);
      const quick = quickFilters.querySelector(`[data-quick-filter="${field}"]`);
      if (source && quick) quick.value = source.value;
    };
    const syncQuickAmenities = () => {
      const selected = new Set(
        [...filterForm.querySelectorAll('[data-filter-amenity]:checked')].map(input => input.value)
      );
      quickFilters.querySelectorAll('[data-quick-amenity]').forEach((input) => {
        input.checked = selected.has(input.value);
      });
    };
    const submitQuickFilters = () => {
      if (typeof filterForm.requestSubmit === 'function') filterForm.requestSubmit();
      else filterForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    };

    ['district', 'priceRange', 'type'].forEach((field) => {
      const source = filterForm.querySelector(`[data-filter-field="${field}"]`);
      const quick = quickFilters.querySelector(`[data-quick-filter="${field}"]`);
      source?.addEventListener('change', () => syncQuickValue(field));
      quick?.addEventListener('change', () => {
        if (source) source.value = quick.value;
        submitQuickFilters();
      });
      syncQuickValue(field);
    });
    filterForm.querySelectorAll('[data-filter-amenity]').forEach((input) => {
      input.addEventListener('change', syncQuickAmenities);
    });
    quickFilters.querySelectorAll('[data-quick-amenity]').forEach((input) => {
      input.addEventListener('change', () => {
        const advancedInput = [...filterForm.querySelectorAll('[data-filter-amenity]')]
          .find(item => item.value === input.value);
        if (advancedInput) advancedInput.checked = input.checked;
        submitQuickFilters();
      });
    });
    syncQuickAmenities();
  }
  if (filterForm) {
    const submitFilters = () => {
      if (typeof filterForm.requestSubmit === 'function') filterForm.requestSubmit();
      else filterForm.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    };
    const typeField = filterForm.querySelector('[data-filter-field="type"]');
    document.querySelectorAll('[data-room-type]').forEach((tab) => {
      tab.addEventListener('click', () => {
        if (!typeField) return;
        typeField.value = tab.dataset.roomType || '';
        submitFilters();
      });
    });

    const sortField = filterForm.querySelector('[data-filter-field="sort"]');
    const quickSort = document.querySelector('[data-quick-filter="sort"]');
    if (sortField && quickSort) {
      quickSort.value = sortField.value;
      quickSort.addEventListener('change', () => {
        sortField.value = quickSort.value;
        submitFilters();
      });
    }
  }

  const roomResultsGrid = document.querySelector('.rooms-grid-list');
  document.querySelectorAll('[data-view-mode]').forEach((button) => {
    button.addEventListener('click', () => {
      if (!roomResultsGrid) return;
      const listView = button.dataset.viewMode === 'list';
      roomResultsGrid.classList.toggle('is-list-view', listView);
      document.querySelectorAll('[data-view-mode]').forEach((item) => {
        const active = item === button;
        item.classList.toggle('active', active);
        item.setAttribute('aria-pressed', String(active));
      });
    });
  });

  const nav = document.getElementById('navbar');
  const scrollTopBtn = document.getElementById('scrollTopBtn');
  const heroSearch = document.getElementById('heroSearch');
  const heroDistrict = document.getElementById('heroDistrict');
  const heroSearchBtn = document.getElementById('heroSearchBtn');
  const roomsGrid = document.getElementById('roomsGrid');
  const districtsGrid = document.getElementById('districtsGrid');
  const testimonialSlider = document.getElementById('testimonialSlider');
  const sliderDots = document.getElementById('sliderDots');
  const blogGrid = document.getElementById('blogGrid');
  const faqGrid = document.getElementById('faqGrid');
  const htmlEncode = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[char]));

  async function loadRoomsFromApi() {
    try {
      const response = await fetch('/api/rooms?limit=10');
      if (!response.ok) throw new Error(`Yêu cầu tải phòng thất bại (${response.status}).`);
      const rooms = await response.json();
      renderRooms(Array.isArray(rooms) ? rooms : []);
    } catch (error) {
      if (roomsGrid) roomsGrid.innerHTML = '<p class="empty-state">Không thể tải danh sách phòng từ máy chủ. Vui lòng thử lại sau.</p>';
      console.error('Không thể tải danh sách phòng từ API.', error);
    }
  }

  async function loadDistrictsFromApi() {
    if (!districtsGrid && !heroDistrict) return;
    try {
      const response = await fetch('/api/districts');
      if (!response.ok) throw new Error(`Yêu cầu tải khu vực thất bại (${response.status}).`);
      const districts = await response.json();
      if (!Array.isArray(districts) || !districts.length) {
        if (districtsGrid) districtsGrid.innerHTML = '<p class="empty-state">Chưa có khu vực nào có phòng được duyệt.</p>';
        return;
      }
      if (heroDistrict) {
        const selectedDistrict = heroDistrict.value;
        heroDistrict.innerHTML = '<option value="">Tất cả khu vực</option>' + districts
          .map(item => `<option value="${htmlEncode(item.name)}">${htmlEncode(item.name)}</option>`)
          .join('');
        if (districts.some(item => item.name === selectedDistrict)) heroDistrict.value = selectedDistrict;
      }
      if (districtsGrid) {
        districtsGrid.innerHTML = districts.map(item => `
          <article class="district-card">
            <div class="section-tag" style="padding: 8px 12px;">📍 Khu vực</div>
            <h3>${htmlEncode(item.name)}</h3>
            <div class="district-stats-table">
              <div><span>Giá trung bình</span><strong>${window.formatPrice(item.averagePrice)}</strong></div>
              <div><span>Phòng có sẵn</span><strong>${Number(item.rooms).toLocaleString('vi-VN')}</strong></div>
            </div>
            <a href="/rooms?district=${encodeURIComponent(item.name)}" class="btn-outline-primary">Xem phòng</a>
          </article>
        `).join('');
      }
    } catch (error) {
      if (districtsGrid) districtsGrid.innerHTML = '<p class="empty-state">Không thể tải dữ liệu khu vực từ máy chủ.</p>';
      console.error('Không thể tải khu vực từ API.', error);
    }
  }

  async function loadRoomStatsFromApi() {
    try {
      const response = await fetch('/api/rooms/stats');
      if (!response.ok) throw new Error(`Yêu cầu tải thống kê thất bại (${response.status}).`);
      const stats = await response.json();
      document.querySelectorAll('.stat-num[data-stat]').forEach((node) => {
        const value = Number(stats[node.dataset.stat]);
        if (Number.isFinite(value)) node.dataset.target = String(value);
      });
    } catch (error) {
      console.error('Không thể tải thống kê từ API.', error);
    }
  }

  function renderRooms(list) {
    if (!roomsGrid) return;
    if (!list.length) {
      roomsGrid.innerHTML = '<p class="empty-state">Hiện chưa có phòng nào được duyệt. Vui lòng quay lại sau.</p>';
      return;
    }
    roomsGrid.innerHTML = list.slice(0, 10).map(room => `
      <article class="room-card">
        <a href="/phong/${encodeURIComponent(room.id)}" class="room-thumb-link">
          <div class="room-thumb" style="background-image: url('${htmlEncode(room.image || '')}')">
            <div class="room-badges">
              <span class="room-tag">${room.verified ? '✓ Đã xác minh' : 'Hot'}</span>
              <span class="room-badge">♥</span>
            </div>
          </div>
        </a>
        <div class="room-info">
          <div class="room-header">
            <h3 class="room-title"><a href="/phong/${encodeURIComponent(room.id)}">${htmlEncode(room.title)}</a></h3>
            <span class="room-price">${window.formatPrice(room.price)}</span>
          </div>
          <div class="room-meta">
            <span>📍 ${htmlEncode(room.district)}</span>
            <span>📐 ${htmlEncode(room.area)}m²</span>
            <span>⭐ ${room.rating ? Number(room.rating).toFixed(1) : 'Chưa đánh giá'}</span>
          </div>
          <div class="room-actions">
            <a class="btn-primary" href="/phong/${encodeURIComponent(room.id)}#booking">Đặt lịch</a>
            <a class="btn-outline-primary" href="/phong/${encodeURIComponent(room.id)}">Xem chi tiết</a>
          </div>
        </div>
      </article>
    `).join('');
  }

  if (nav) {
    window.addEventListener('scroll', () => nav.classList.toggle('scrolled', window.scrollY > 24));
  }

  if (scrollTopBtn) {
    window.addEventListener('scroll', () => {
      scrollTopBtn.style.display = window.scrollY > 500 ? 'grid' : 'none';
    });
    scrollTopBtn.style.display = 'none';
  }

  function navigateToRooms(filter) {
    const params = new URLSearchParams();
    const keyword = heroSearch?.value.trim();
    const districtOption = heroDistrict?.selectedOptions[0];
    const district = districtOption?.value ? districtOption.textContent?.trim() : '';
    if (keyword) params.set('keyword', keyword);
    if (district) params.set('district', district);
    if (filter === 'single' || filter === 'shared' || filter === 'mini') params.set('type', filter);
    if (filter === 'cheap') params.set('maxPrice', '3000000');
    if (filter === 'ac') params.set('amenity', 'Máy lạnh');
    const url = `/rooms${params.size ? `?${params}` : ''}`;
    if (window.__troTotNavigate) window.__troTotNavigate(url);
    else window.location.href = url;
  }

  if (heroSearch) {
    heroSearch.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') navigateToRooms();
    });
  }
  heroSearchBtn?.addEventListener('click', () => navigateToRooms());

  function renderTestimonials() {
    if (!testimonialSlider || !sliderDots) return;
    testimonialSlider.innerHTML = data.testimonials.map((item, index) => `
      <article class="testimonial-card" style="display: ${index === 0 ? 'block' : 'none'};">
        <div class="rating" style="color: #f59e0b; margin-bottom: 12px; font-size: 1.2rem;">${'★'.repeat(Math.round(item.rating))}</div>
        <p style="font-size: 1.05rem; color: var(--muted); margin: 0 0 18px;">“${item.text}”</p>
        <div style="display: flex; align-items: center; justify-content: space-between; border-top: 1px solid rgba(148,163,184,0.2); padding-top: 16px;">
          <strong>${item.name}</strong>
          <span style="color: var(--primary-strong); font-weight: 700;">Khách hàng</span>
        </div>
      </article>
    `).join('');

    sliderDots.innerHTML = data.testimonials.map((_, index) => `<button class="${index === 0 ? 'active' : ''}" data-index="${index}" aria-label="Slide ${index + 1}"></button>`).join('');

    sliderDots.querySelectorAll('button').forEach((button) => {
      button.addEventListener('click', () => {
        const index = Number(button.dataset.index);
        const slides = testimonialSlider.querySelectorAll('.testimonial-card');
        slides.forEach((slide, i) => { slide.style.display = i === index ? 'block' : 'none'; });
        sliderDots.querySelectorAll('button').forEach(dot => dot.classList.toggle('active', dot === button));
      });
    });
  }

  function renderBlogPosts() {
    if (!blogGrid) return;
    blogGrid.innerHTML = data.blogPosts.map(post => `
      <article class="blog-card">
        <div class="article-thumb" style="background-image: url('${post.image}')"></div>
        <div class="article-content">
          <span class="article-category">${post.category}</span>
          <h3 class="article-title">${post.title}</h3>
          <p class="article-desc">Mẹo sống, trải nghiệm thực tế và cách tối ưu chi phí cho chỗ ở của bạn.</p>
          <div class="article-footer">
            <span>${post.readTime}</span>
            <a href="/blog" class="read-more-btn">Đọc thêm →</a>
          </div>
        </div>
      </article>
    `).join('');
  }

  function renderFaqs() {
    if (!faqGrid) return;
    faqGrid.innerHTML = data.faqs.map((item, index) => `
      <article class="faq-card" style="padding: 22px;">
        <div class="section-tag" style="padding: 8px 12px; margin-bottom: 12px;">Q${index + 1}</div>
        <h3 style="margin: 0 0 10px;">${item.q}</h3>
        <p style="margin: 0; color: var(--muted);">${item.a}</p>
      </article>
    `).join('');
  }

  renderTestimonials();
  renderBlogPosts();
  renderFaqs();
  await Promise.all([
    roomsGrid ? loadRoomsFromApi() : Promise.resolve(),
    districtsGrid || heroDistrict ? loadDistrictsFromApi() : Promise.resolve(),
    loadRoomStatsFromApi()
  ]);

  document.querySelectorAll('.filter-chip').forEach((button) => {
    button.addEventListener('click', () => {
      document.querySelectorAll('.filter-chip').forEach(item => item.classList.toggle('active', item === button));
      navigateToRooms(button.dataset.filter);
    });
  });

  const statNumbers = document.querySelectorAll('.stat-num[data-target]');
  statNumbers.forEach((node) => {
    const target = Number(node.dataset.target || 0);
    let current = 0;
    const step = Math.max(1, Math.ceil(target / 60));
    const timer = setInterval(() => {
      current += step;
      if (current >= target) {
        current = target;
        clearInterval(timer);
      }
      node.textContent = current.toLocaleString('vi-VN');
      if (target < 20) node.textContent = current;
    }, 18);
  });

  const mobileNav = document.getElementById('navHamburger');
  const navLinks = document.getElementById('navLinks');
  if (mobileNav && navLinks) {
    mobileNav.addEventListener('click', () => {
      const next = navLinks.style.display === 'flex' ? 'none' : 'flex';
      navLinks.style.display = next;
      navLinks.style.position = 'absolute';
      navLinks.style.top = '80px';
      navLinks.style.left = '16px';
      navLinks.style.right = '16px';
      navLinks.style.flexDirection = 'column';
      navLinks.style.padding = '12px';
      navLinks.style.background = 'rgba(15,23,42,0.9)';
      navLinks.style.borderRadius = '18px';
      navLinks.style.border = '1px solid rgba(148,163,184,0.2)';
    });
  }
};

if (!window.__troTotMainInitialized) {
  window.__troTotMainInitialized = true;
  document.addEventListener('DOMContentLoaded', window.initializeTroTotPage);
  document.addEventListener('troto:page-loaded', window.initializeTroTotPage);
}

window.openModal = function (id) {
  const modal = document.getElementById(id);
  if (!modal) return;
  modal.classList.add('show');
};

window.closeModal = function (id) {
  const modal = document.getElementById(id);
  if (!modal) return;
  modal.classList.remove('show');
};

window.openLiveChat = function () {
  window.alert('💬 Đội ngũ hỗ trợ sẽ phản hồi trong vài phút.');
};

window.clearFilters = function () {
  document.querySelectorAll('.filter-check input').forEach((input) => (input.checked = false));
  const keyword = document.getElementById('filterKeyword');
  if (keyword) keyword.value = '';
  const priceMin = document.getElementById('priceMin');
  if (priceMin) priceMin.value = '';
  const priceMax = document.getElementById('priceMax');
  if (priceMax) priceMax.value = '';
  const result = document.getElementById('resultsCount');
  if (result) result.textContent = 'Hiển thị tất cả phòng';
};

window.applyFilters = function () {
  const count = document.querySelectorAll('.room-card').length;
  const output = document.getElementById('resultsCount');
  if (output) output.textContent = `Hiển thị ${count} kết quả phù hợp`;
};

window.setView = function (mode) {
  const cards = document.querySelectorAll('.room-card');
  cards.forEach(card => card.classList.toggle('list-view', mode === 'list'));
  document.getElementById('gridViewBtn')?.classList.toggle('active', mode === 'grid');
  document.getElementById('listViewBtn')?.classList.toggle('active', mode === 'list');
};

window.toggleSidebar = function () {
  const sidebar = document.getElementById('roomsSidebar');
  if (!sidebar) return;
  sidebar.style.display = sidebar.style.display === 'none' ? 'block' : 'none';
};

window.sortRooms = function () {
  const select = document.getElementById('sortSelect');
  if (!select) return;
  const value = select.value;
  const cards = Array.from(document.querySelectorAll('.room-card'));
  if (!cards.length) return;
  cards.sort((a, b) => {
    const priceA = Number(a.querySelector('.room-price')?.textContent.replace(/[^\d]/g, '') || 0);
    const priceB = Number(b.querySelector('.room-price')?.textContent.replace(/[^\d]/g, '') || 0);
    if (value === 'price-asc') return priceA - priceB;
    if (value === 'price-desc') return priceB - priceA;
    return 0;
  });
  const parent = cards[0]?.parentElement;
  if (parent) cards.forEach(card => parent.appendChild(card));
};

window.showToast = function (msg, type = 'success') {
  const toast = document.getElementById('toast');
  if (!toast) return;
  toast.textContent = msg;
  toast.className = 'toast ' + type;
  toast.style.display = 'block';
  setTimeout(() => toast.style.display = 'none', 3200);
};

window.socialLogin = function (provider) {
  window.showToast(`🔗 Đang kết nối với ${provider === 'google' ? 'Google' : 'Facebook'}...`, 'success');
  setTimeout(() => window.location.href = '/', 1200);
};

window.togglePass = function (id) {
  const input = document.getElementById(id);
  if (!input) return;
  input.type = input.type === 'password' ? 'text' : 'password';
};

window.selectRole = function (role, button) {
  const hidden = document.getElementById('selectedRole');
  if (hidden) hidden.value = role;
  document.querySelectorAll('.role-btn').forEach((btn) => btn.classList.toggle('active', btn === button));
};

window.checkRegister = function () {
  const pass = document.getElementById('regPass');
  const pass2 = document.getElementById('regPass2');
  if (!pass || !pass2) return true;
  if (pass.value !== pass2.value) {
    window.showToast('❌ Mật khẩu xác nhận không khớp!', 'error');
    return false;
  }
  if (pass.value.length < 8) {
    window.showToast('❌ Mật khẩu phải có ít nhất 8 ký tự!', 'error');
    return false;
  }
  return true;
};

window.calculateCost = function () {
  const rent = Number(document.getElementById('calcRent')?.value || 0);
  const elecKwh = Number(document.getElementById('calcElecKwh')?.value || 0);
  const elecPrice = Number(document.getElementById('calcElecPrice')?.value || 0);
  const water = Number(document.getElementById('calcWater')?.value || 0);
  const internet = Number(document.getElementById('calcInternet')?.value || 0);
  const parking = Number(document.getElementById('calcParking')?.value || 0);

  const electricalTotal = elecKwh * elecPrice;
  const total = rent + electricalTotal + water + internet + parking;
  const result = document.getElementById('calcResult');
  const breakdown = document.getElementById('calcBreakdown');

  if (result) {
    result.textContent = `${window.formatPrice(total)}`;
  }

  if (breakdown) {
    breakdown.innerHTML = `
      <div class="breakdown-item"><span>Tiền phòng</span><strong>${window.formatPrice(rent)}</strong></div>
      <div class="breakdown-item"><span>Điện</span><strong>${window.formatPrice(electricalTotal)}</strong></div>
      <div class="breakdown-item"><span>Nước</span><strong>${window.formatPrice(water)}</strong></div>
      <div class="breakdown-item"><span>Internet</span><strong>${window.formatPrice(internet)}</strong></div>
      <div class="breakdown-item"><span>Gửi xe</span><strong>${window.formatPrice(parking)}</strong></div>
    `;
  }
};

window.showSuggestions = async function () {
  const resultBox = document.getElementById('suggestResults');
  const roomGrid = document.getElementById('suggestRoomsGrid');
  if (!resultBox || !roomGrid) return;

  const button = document.getElementById('btnSuggest');
  const originalLabel = button?.textContent;
  if (button) {
    button.disabled = true;
    button.textContent = 'Đang tìm phòng phù hợp...';
  }

  try {
    const params = new URLSearchParams({
      limit: '12',
      maxPrice: document.getElementById('suggestBudget')?.value || '',
      type: document.getElementById('suggestType')?.value || ''
    });
    const location = document.getElementById('suggestLocation')?.value.trim();
    if (location) params.set('keyword', location);

    const amenityMap = {
      am1: 'Máy lạnh',
      am2: 'Wifi',
      am3: 'Gác lửng',
      am4: 'Chỗ để xe',
      am5: 'Giờ giấc tự do',
      am6: 'Nuôi thú cưng'
    };
    Object.entries(amenityMap).forEach(([id, value]) => {
      if (document.getElementById(id)?.checked) params.append('amenity', value);
    });

    const response = await fetch(`/api/rooms?${params}`);
    if (!response.ok) throw new Error(`Yêu cầu tìm phòng thất bại (${response.status}).`);
    const rooms = await response.json();
    const picks = Array.isArray(rooms) ? rooms.slice(0, 3) : [];
    roomGrid.innerHTML = picks.length ? picks.map(room => `
      <article class="suggest-room-card" style="padding: 14px; border-radius: 18px; background: white; border: 1px solid rgba(148,163,184,0.2);">
        <div style="display:flex; justify-content:space-between; gap:10px; align-items:start; margin-bottom: 8px;">
          <strong><a href="/phong/${encodeURIComponent(room.id)}" style="color: var(--text);">${String(room.title ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}</a></strong>
          <span style="color: var(--primary-strong); font-weight: 800;">${window.formatPrice(room.price)}</span>
        </div>
        <div style="font-size: 0.9rem; color: var(--muted);">📍 ${String(room.district ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))} • 📐 ${room.area}m²</div>
      </article>
    `).join('') : '<p class="empty-state">Chưa tìm thấy phòng phù hợp. Hãy thử nới rộng ngân sách hoặc giảm tiện nghi bắt buộc.</p>';
  } catch (error) {
    roomGrid.innerHTML = '<p class="empty-state">Không thể tìm phòng từ máy chủ. Vui lòng thử lại sau.</p>';
    console.error('Không thể tải gợi ý phòng từ API.', error);
  } finally {
    if (button) {
      button.disabled = false;
      button.textContent = originalLabel || 'Tìm Phòng Phù Hợp Cho Tôi';
    }
  }

  resultBox.style.display = 'block';
};
