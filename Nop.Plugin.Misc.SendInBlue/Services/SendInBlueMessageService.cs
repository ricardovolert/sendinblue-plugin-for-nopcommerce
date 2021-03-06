﻿using System;
using System.Collections.Generic;
using Nop.Core;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Forums;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.News;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Vendors;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.SendInBlue.Services
{
    /// <summary>
    /// SendInBlue message service
    /// </summary>
    public class SendInBlueMessageService : WorkflowMessageService
    {
        #region Fields

        private readonly IEmailAccountService _emailAccountService;
        private readonly IEventPublisher _eventPublisher;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IMessageTemplateService _messageTemplateService;
        private readonly IMessageTokenProvider _messageTokenProvider;
        private readonly IQueuedEmailService _queuedEmailService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IStoreService _storeService;
        private readonly ITokenizer _tokenizer;
        private readonly SendInBlueEmailManager _sendInBlueEmailManager;

        #endregion

        #region Ctor

        public SendInBlueMessageService(
            IMessageTemplateService messageTemplateService,
            IQueuedEmailService queuedEmailService,
            ILanguageService languageService,
            ITokenizer tokenizer,
            IEmailAccountService emailAccountService,
            IMessageTokenProvider messageTokenProvider,
            IStoreService storeService,
            IStoreContext storeContext,
            EmailAccountSettings emailAccountSettings,
            IEventPublisher eventPublisher,
            ISettingService settingService,
            IGenericAttributeService genericAttributeService,
            SendInBlueEmailManager sendInBlueEmailManager)
            : base(messageTemplateService,
                queuedEmailService,
                languageService,
                tokenizer,
                emailAccountService,
                messageTokenProvider,
                storeService,
                storeContext,
                emailAccountSettings,
                eventPublisher)
        {
            this._emailAccountService = emailAccountService;
            this._eventPublisher = eventPublisher;
            this._genericAttributeService = genericAttributeService;
            this._messageTemplateService = messageTemplateService;
            this._messageTokenProvider = messageTokenProvider;
            this._queuedEmailService = queuedEmailService;
            this._settingService = settingService;
            this._storeContext = storeContext;
            this._storeService = storeService;
            this._tokenizer = tokenizer;
            this._sendInBlueEmailManager = sendInBlueEmailManager;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Send email or SMS notification
        /// </summary>
        /// <param name="storeId">Store identifier</param>
        /// <param name="languageId">Language identifier</param>
        /// <param name="messageName">System name of the message template</param>
        /// <param name="tokenModel">Token model</param>
        /// <param name="toEmail">Recipient email</param>
        /// <param name="toName">Recipient name</param>
        /// <param name="attachmentFilePath">Attachment file path</param>
        /// <param name="attachmentFileName">Attachment file name. If specified, then this file name will be sent to a recipient. Otherwise, "AttachmentFilePath" name will be used.</param>
        /// <returns>Queued email identifier</returns>
        protected int SendNotification(int storeId, int languageId, string messageName, TokenModel tokenModel,
            string toEmail = null, string toName = null, string attachmentFilePath = null, string attachmentFileName = null)
        {
            var store = storeId > 0 ? _storeService.GetStoreById(storeId) ?? _storeContext.CurrentStore : _storeContext.CurrentStore;
            var sendInBlueSettings = _settingService.LoadSetting<SendInBlueSettings>(store.Id);

            languageId = EnsureLanguageIsActive(languageId, store.Id);

            var messageTemplate = _messageTemplateService.GetMessageTemplateByName(messageName, store.Id);
            if (messageTemplate == null)
                return 0;

            //ensure that email or sms message is active
            var useSms = sendInBlueSettings.UseSMS && messageTemplate.GetAttribute<bool>("UseSMS", _genericAttributeService);
            if (!useSms && !messageTemplate.IsActive)
                return 0;

            #region Email account

            //get email account from settings
            EmailAccount emailAccount = null;
            if (sendInBlueSettings.UseSendInBlueSMTP)
                emailAccount = _emailAccountService.GetEmailAccountById(sendInBlueSettings.SendInBlueEmailAccountId);
            if (emailAccount == null)
                emailAccount = GetEmailAccountOfMessageTemplate(messageTemplate, languageId);

            #endregion

            #region Tokens

            //add tokens
            var tokens = tokenModel.Tokens;
            _messageTokenProvider.AddStoreTokens(tokens, store, emailAccount);
            if (tokenModel.Customer != null)
                _messageTokenProvider.AddCustomerTokens(tokens, tokenModel.Customer);
            if (tokenModel.Order != null)
                _messageTokenProvider.AddOrderTokens(tokens, tokenModel.Order, languageId);
            if (tokenModel.Shipment != null)
                _messageTokenProvider.AddShipmentTokens(tokens, tokenModel.Shipment, languageId);
            if (tokenModel.Order != null)
                _messageTokenProvider.AddOrderRefundedTokens(tokens, tokenModel.Order, tokenModel.RefundedAmount);
            if (tokenModel.OrderNote != null)
                _messageTokenProvider.AddOrderNoteTokens(tokens, tokenModel.OrderNote);
            if (tokenModel.RecurringPayment != null)
                _messageTokenProvider.AddRecurringPaymentTokens(tokens, tokenModel.RecurringPayment);
            if (tokenModel.NewsLetterSubscription != null)
                _messageTokenProvider.AddNewsLetterSubscriptionTokens(tokens, tokenModel.NewsLetterSubscription);
            if (tokenModel.ReturnRequest != null)
                _messageTokenProvider.AddReturnRequestTokens(tokens, tokenModel.ReturnRequest, tokenModel.OrderItem);
            if (tokenModel.Forum != null)
                _messageTokenProvider.AddForumTokens(tokens, tokenModel.Forum);
            if (tokenModel.ForumTopic != null)
                _messageTokenProvider.AddForumTopicTokens(tokens, tokenModel.ForumTopic);
            if (tokenModel.ForumPost != null)
            {
                _messageTokenProvider.AddForumPostTokens(tokens, tokenModel.ForumPost);
                _messageTokenProvider.AddForumTopicTokens(tokens, tokenModel.ForumTopic, tokenModel.FriendlyForumTopicPageIndex, tokenModel.ForumPost.Id);
            }
            if (tokenModel.PrivateMessage != null)
                _messageTokenProvider.AddPrivateMessageTokens(tokens, tokenModel.PrivateMessage);
            if (tokenModel.Vendor != null)
                _messageTokenProvider.AddVendorTokens(tokens, tokenModel.Vendor);
            if (tokenModel.GiftCard != null)
                _messageTokenProvider.AddGiftCardTokens(tokens, tokenModel.GiftCard);
            if (tokenModel.ProductReview != null)
                _messageTokenProvider.AddProductReviewTokens(tokens, tokenModel.ProductReview);
            if (tokenModel.Product != null)
                _messageTokenProvider.AddProductTokens(tokens, tokenModel.Product, languageId);
            if (tokenModel.Combination != null)
                _messageTokenProvider.AddAttributeCombinationTokens(tokens, tokenModel.Combination, languageId);
            if (tokenModel.BlogComment != null)
                _messageTokenProvider.AddBlogCommentTokens(tokens, tokenModel.BlogComment);
            if (tokenModel.NewsComment != null)
                _messageTokenProvider.AddNewsCommentTokens(tokens, tokenModel.NewsComment);
            if (tokenModel.Subscription != null)
                _messageTokenProvider.AddBackInStockTokens(tokens, tokenModel.Subscription);

            //event notification
            _eventPublisher.MessageTokensAdded(messageTemplate, tokens);

            #endregion

            #region SMS

            //send SMS
            if (useSms)
            {
                //get text with replaced tokens
                var text = messageTemplate.GetAttribute<string>("SMSText", _genericAttributeService);
                if (!string.IsNullOrEmpty(text))
                    text = _tokenizer.Replace(text, tokens, false);

                //get phone number
                var phoneNumberTo = sendInBlueSettings.MyPhoneNumber;
                switch (messageTemplate.GetAttribute<int>("PhoneTypeId", _genericAttributeService))
                {
                    case 1:
                        phoneNumberTo = tokenModel.Customer != null ? 
                            tokenModel.Customer.GetAttribute<string>(SystemCustomerAttributeNames.Phone) : null;
                        break;
                    case 2:
                        phoneNumberTo = tokenModel.BillingAddress != null ? tokenModel.BillingAddress.PhoneNumber : null;
                        break;
                    default:
                        break;
                }
                _sendInBlueEmailManager.SendSMS(phoneNumberTo, sendInBlueSettings.SMSFrom, text);
            }

            #endregion

            #region Email

            if (!messageTemplate.IsActive)
                return 0;
                     
            //use standard way for the sending emails
            if (!sendInBlueSettings.UseSendInBlueSMTP || !messageTemplate.GetAttribute<bool>("SendInBlueTemplate", _genericAttributeService))
                return base.SendNotification(messageTemplate, emailAccount, languageId, tokens,
                    toEmail ?? emailAccount.Email, toName ?? emailAccount.DisplayName, attachmentFilePath, attachmentFileName);

            //or use SendInBlue service
            //get message template 
            var email = _sendInBlueEmailManager.GetQueuedEmailFromTemplate (messageTemplate.GetAttribute<int>("TemplateId", _genericAttributeService));
            if (email == null)
                return 0;

            //replace tokens in the body and in the subject
            if (!string.IsNullOrEmpty(email.Subject))
                email.Subject = _tokenizer.Replace(email.Subject, tokens, false);
            if (!string.IsNullOrEmpty(email.Body))
                email.Body = _tokenizer.Replace(email.Body, tokens, true);

            //set email parameters
            email.Priority = QueuedEmailPriority.High;
            email.To = toEmail ?? emailAccount.Email;
            email.ToName = toName ?? emailAccount.DisplayName;
            email.CC = string.Empty;
            email.Bcc = messageTemplate.BccEmailAddresses;
            email.AttachmentFilePath = attachmentFilePath;
            email.AttachmentFileName = attachmentFileName;
            email.AttachedDownloadId = messageTemplate.AttachedDownloadId;
            email.CreatedOnUtc = DateTime.UtcNow;
            email.EmailAccountId = emailAccount.Id;
            _queuedEmailService.InsertQueuedEmail(email);

            return email.Id;

            #endregion
        }

        #endregion

        #region Methods

        #region Customer workflow

        /// <summary>
        /// Sends 'New customer' notification message to a store owner
        /// </summary>
        /// <param name="customer">Customer instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendCustomerRegisteredNotificationMessage(Customer customer, int languageId)
        {
            return SendNotification(0, languageId, "NewCustomer.Notification",
                new TokenModel { Customer = customer });
        }

        /// <summary>
        /// Sends a welcome message to a customer
        /// </summary>
        /// <param name="customer">Customer instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendCustomerWelcomeMessage(Customer customer, int languageId)
        {
            return SendNotification(0, languageId, "Customer.WelcomeMessage",
                new TokenModel { Customer = customer, BillingAddress = customer.BillingAddress },
                customer.Email, customer.GetFullName());
        }

        /// <summary>
        /// Sends an email validation message to a customer
        /// </summary>
        /// <param name="customer">Customer instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendCustomerEmailValidationMessage(Customer customer, int languageId)
        {
            return SendNotification(0, languageId, "Customer.EmailValidationMessage",
                new TokenModel { Customer = customer, BillingAddress = customer.BillingAddress },
                customer.Email, customer.GetFullName());
        }

        /// <summary>
        /// Sends password recovery message to a customer
        /// </summary>
        /// <param name="customer">Customer instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendCustomerPasswordRecoveryMessage(Customer customer, int languageId)
        {
            return SendNotification(0, languageId, "Customer.PasswordRecovery",
                new TokenModel { Customer = customer, BillingAddress = customer.BillingAddress },
                customer.Email, customer.GetFullName());
        }

        #endregion

        #region Order workflow

        /// <summary>
        /// Sends an order placed notification to a vendor
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="vendor">Vendor instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderPlacedVendorNotification(Order order, Vendor vendor, int languageId)
        {
            return SendNotification(order.StoreId, languageId, "OrderPlaced.VendorNotification",
                new TokenModel { Customer = order.Customer, Order = order },
                vendor.Email, vendor.Name);
        }

        /// <summary>
        /// Sends an order placed notification to a store owner
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderPlacedStoreOwnerNotification(Order order, int languageId)
        {
            return SendNotification(order.StoreId, languageId, "OrderPlaced.StoreOwnerNotification",
                new TokenModel { Customer = order.Customer, Order = order });
        }

        /// <summary>
        /// Sends an order paid notification to a store owner
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderPaidStoreOwnerNotification(Order order, int languageId)
        {
            return SendNotification(order.StoreId, languageId, "OrderPaid.StoreOwnerNotification",
                new TokenModel { Customer = order.Customer, Order = order });
        }

        /// <summary>
        /// Sends an order paid notification to a customer
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <param name="attachmentFilePath">Attachment file path</param>
        /// <param name="attachmentFileName">Attachment file name. If specified, then this file name will be sent to a recipient. Otherwise, "AttachmentFilePath" name will be used.</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderPaidCustomerNotification(Order order, int languageId,
            string attachmentFilePath = null, string attachmentFileName = null)
        {
            return SendNotification(order.StoreId, languageId, "OrderPaid.CustomerNotification",
                new TokenModel { Customer = order.Customer, BillingAddress = order.BillingAddress, Order = order },
                order.BillingAddress.Email, string.Format("{0} {1}", order.BillingAddress.FirstName, order.BillingAddress.LastName),
                attachmentFilePath, attachmentFileName);
        }

        /// <summary>
        /// Sends an order paid notification to a vendor
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="vendor">Vendor instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderPaidVendorNotification(Order order, Vendor vendor, int languageId)
        {
            return SendNotification(order.StoreId, languageId, "OrderPaid.VendorNotification",
                new TokenModel { Customer = order.Customer, Order = order },
                vendor.Email, vendor.Name);
        }

        /// <summary>
        /// Sends an order placed notification to a customer
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <param name="attachmentFilePath">Attachment file path</param>
        /// <param name="attachmentFileName">Attachment file name. If specified, then this file name will be sent to a recipient. Otherwise, "AttachmentFilePath" name will be used.</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderPlacedCustomerNotification(Order order, int languageId,
            string attachmentFilePath = null, string attachmentFileName = null)
        {
            return SendNotification(order.StoreId, languageId, "OrderPlaced.CustomerNotification",
                new TokenModel { Customer = order.Customer, BillingAddress = order.BillingAddress, Order = order },
                order.BillingAddress.Email, string.Format("{0} {1}", order.BillingAddress.FirstName, order.BillingAddress.LastName),
                attachmentFilePath, attachmentFileName);
        }

        /// <summary>
        /// Sends a shipment sent notification to a customer
        /// </summary>
        /// <param name="shipment">Shipment</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendShipmentSentCustomerNotification(Shipment shipment, int languageId)
        {
            return SendNotification(shipment.Order.StoreId, languageId, "ShipmentSent.CustomerNotification",
                new TokenModel { Customer = shipment.Order.Customer, BillingAddress = shipment.Order.BillingAddress,
                    Order = shipment.Order, Shipment = shipment },
                shipment.Order.BillingAddress.Email,
                string.Format("{0} {1}", shipment.Order.BillingAddress.FirstName, shipment.Order.BillingAddress.LastName));
        }

        /// <summary>
        /// Sends a shipment delivered notification to a customer
        /// </summary>
        /// <param name="shipment">Shipment</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendShipmentDeliveredCustomerNotification(Shipment shipment, int languageId)
        {
            return SendNotification(shipment.Order.StoreId, languageId, "ShipmentDelivered.CustomerNotification",
                new TokenModel { Customer = shipment.Order.Customer, BillingAddress = shipment.Order.BillingAddress,
                    Order = shipment.Order, Shipment = shipment },
                shipment.Order.BillingAddress.Email,
                string.Format("{0} {1}", shipment.Order.BillingAddress.FirstName, shipment.Order.BillingAddress.LastName));
        }

        /// <summary>
        /// Sends an order completed notification to a customer
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <param name="attachmentFilePath">Attachment file path</param>
        /// <param name="attachmentFileName">Attachment file name. If specified, then this file name will be sent to a recipient. Otherwise, "AttachmentFilePath" name will be used.</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderCompletedCustomerNotification(Order order, int languageId,
            string attachmentFilePath = null, string attachmentFileName = null)
        {
            return SendNotification(order.StoreId, languageId, "OrderCompleted.CustomerNotification",
                new TokenModel { Customer = order.Customer, BillingAddress = order.BillingAddress, Order = order },
                order.BillingAddress.Email, string.Format("{0} {1}", order.BillingAddress.FirstName, order.BillingAddress.LastName),
                attachmentFilePath, attachmentFileName);
        }

        /// <summary>
        /// Sends an order cancelled notification to a customer
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderCancelledCustomerNotification(Order order, int languageId)
        {
            return SendNotification(order.StoreId, languageId, "OrderCancelled.CustomerNotification",
                new TokenModel { Customer = order.Customer, BillingAddress = order.BillingAddress, Order = order },
                order.BillingAddress.Email, string.Format("{0} {1}", order.BillingAddress.FirstName, order.BillingAddress.LastName));
        }

        /// <summary>
        /// Sends an order refunded notification to a store owner
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="refundedAmount">Amount refunded</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderRefundedStoreOwnerNotification(Order order, decimal refundedAmount, int languageId)
        {
            return SendNotification(order.StoreId, languageId, "OrderRefunded.StoreOwnerNotification",
                new TokenModel { Customer = order.Customer, Order = order, RefundedAmount = refundedAmount });
        }

        /// <summary>
        /// Sends an order refunded notification to a customer
        /// </summary>
        /// <param name="order">Order instance</param>
        /// <param name="refundedAmount">Amount refunded</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendOrderRefundedCustomerNotification(Order order, decimal refundedAmount, int languageId)
        {
            return SendNotification(order.StoreId, languageId, "OrderRefunded.CustomerNotification",
                new TokenModel { Customer = order.Customer, BillingAddress = order.BillingAddress,
                    Order = order, RefundedAmount = refundedAmount },
                order.BillingAddress.Email, string.Format("{0} {1}", order.BillingAddress.FirstName, order.BillingAddress.LastName));
        }

        /// <summary>
        /// Sends a new order note added notification to a customer
        /// </summary>
        /// <param name="orderNote">Order note</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendNewOrderNoteAddedCustomerNotification(OrderNote orderNote, int languageId)
        {
            return SendNotification(orderNote.Order.StoreId, languageId, "Customer.NewOrderNote",
                new TokenModel { Customer = orderNote.Order.Customer, BillingAddress = orderNote.Order.BillingAddress,
                    Order = orderNote.Order, OrderNote = orderNote },
                orderNote.Order.BillingAddress.Email,
                string.Format("{0} {1}", orderNote.Order.BillingAddress.FirstName, orderNote.Order.BillingAddress.LastName));
        }

        /// <summary>
        /// Sends a "Recurring payment cancelled" notification to a store owner
        /// </summary>
        /// <param name="recurringPayment">Recurring payment</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendRecurringPaymentCancelledStoreOwnerNotification(RecurringPayment recurringPayment, int languageId)
        {
            return SendNotification(recurringPayment.InitialOrder.StoreId, languageId, "RecurringPaymentCancelled.StoreOwnerNotification",
                new TokenModel { Customer = recurringPayment.InitialOrder.Customer,
                    Order = recurringPayment.InitialOrder, RecurringPayment = recurringPayment });
        }

        #endregion

        #region Newsletter workflow

        /// <summary>
        /// Sends a newsletter subscription activation message
        /// </summary>
        /// <param name="subscription">Newsletter subscription</param>
        /// <param name="languageId">Language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendNewsLetterSubscriptionActivationMessage(NewsLetterSubscription subscription, int languageId)
        {
            return SendNotification(0, languageId, "NewsLetterSubscription.ActivationMessage",
                new TokenModel { NewsLetterSubscription = subscription }, subscription.Email, string.Empty);
        }

        /// <summary>
        /// Sends a newsletter subscription deactivation message
        /// </summary>
        /// <param name="subscription">Newsletter subscription</param>
        /// <param name="languageId">Language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendNewsLetterSubscriptionDeactivationMessage(NewsLetterSubscription subscription, int languageId)
        {
            return SendNotification(0, languageId, "NewsLetterSubscription.DeactivationMessage",
                new TokenModel { NewsLetterSubscription = subscription }, subscription.Email, string.Empty);
        }

        #endregion

        #region Send a message to a friend

        /// <summary>
        /// Sends "email a friend" message
        /// </summary>
        /// <param name="customer">Customer instance</param>
        /// <param name="languageId">Message language identifier</param>
        /// <param name="product">Product instance</param>
        /// <param name="customerEmail">Customer's email</param>
        /// <param name="friendsEmail">Friend's email</param>
        /// <param name="personalMessage">Personal message</param>
        /// <returns>Queued email identifier</returns>
        public override int SendProductEmailAFriendMessage(Customer customer, int languageId,
            Product product, string customerEmail, string friendsEmail, string personalMessage)
        {
            return SendNotification(0, languageId, "Service.EmailAFriend",
                new TokenModel { Tokens = new List<Token> { new Token("EmailAFriend.PersonalMessage", personalMessage, true),
                    new Token("EmailAFriend.Email", customerEmail) }, Customer = customer },
                friendsEmail, string.Empty);
        }

        /// <summary>
        /// Sends wishlist "email a friend" message
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="languageId">Message language identifier</param>
        /// <param name="customerEmail">Customer's email</param>
        /// <param name="friendsEmail">Friend's email</param>
        /// <param name="personalMessage">Personal message</param>
        /// <returns>Queued email identifier</returns>
        public override int SendWishlistEmailAFriendMessage(Customer customer, int languageId,
             string customerEmail, string friendsEmail, string personalMessage)
        {
            return SendNotification(0, languageId, "Wishlist.EmailAFriend",
                new TokenModel { Tokens = new List<Token> { new Token("Wishlist.PersonalMessage", personalMessage, true),
                    new Token("Wishlist.Email", customerEmail) }, Customer = customer },
                friendsEmail, string.Empty);
        }

        #endregion

        #region Return requests

        /// <summary>
        /// Sends 'New Return Request' message to a store owner
        /// </summary>
        /// <param name="returnRequest">Return request</param>
        /// <param name="orderItem">Order item</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendNewReturnRequestStoreOwnerNotification(ReturnRequest returnRequest, OrderItem orderItem, int languageId)
        {
            return SendNotification(orderItem.Order.StoreId, languageId, "NewReturnRequest.StoreOwnerNotification",
                new TokenModel { Customer = returnRequest.Customer, OrderItem = orderItem, ReturnRequest = returnRequest });
        }

        /// <summary>
        /// Sends 'Return Request status changed' message to a customer
        /// </summary>
        /// <param name="returnRequest">Return request</param>
        /// <param name="orderItem">Order item</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendReturnRequestStatusChangedCustomerNotification(ReturnRequest returnRequest,
            OrderItem orderItem, int languageId)
        {
            return SendNotification(orderItem.Order.StoreId, languageId, "ReturnRequestStatusChanged.CustomerNotification",
                new TokenModel { Customer = returnRequest.Customer, BillingAddress = orderItem.Order.BillingAddress,
                    OrderItem = orderItem, ReturnRequest = returnRequest },
                returnRequest.Customer.IsGuest() ? orderItem.Order.BillingAddress.Email : returnRequest.Customer.Email,
                returnRequest.Customer.IsGuest() ? orderItem.Order.BillingAddress.FirstName : returnRequest.Customer.GetFullName());
        }

        #endregion

        #region Forum Notifications

        /// <summary>
        /// Sends a forum subscription message to a customer
        /// </summary>
        /// <param name="customer">Customer instance</param>
        /// <param name="forumTopic">Forum Topic</param>
        /// <param name="forum">Forum</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public int SendNewForumTopicMessage(Customer customer, ForumTopic forumTopic, Forum forum, int languageId)
        {
            return SendNotification(0, languageId, "Forums.NewForumTopic",
                new TokenModel { Customer = customer, BillingAddress = customer.BillingAddress,
                    ForumTopic = forumTopic, Forum = forumTopic.Forum },
                customer.Email, customer.GetFullName());
        }

        /// <summary>
        /// Sends a forum subscription message to a customer
        /// </summary>
        /// <param name="customer">Customer instance</param>
        /// <param name="forumPost">Forum post</param>
        /// <param name="forumTopic">Forum Topic</param>
        /// <param name="forum">Forum</param>
        /// <param name="friendlyForumTopicPageIndex">Friendly (starts with 1) forum topic page to use for URL generation</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public int SendNewForumPostMessage(Customer customer, ForumPost forumPost, ForumTopic forumTopic,
            Forum forum, int friendlyForumTopicPageIndex, int languageId)
        {
            return SendNotification(0, languageId, "Forums.NewForumPost",
                new TokenModel { Customer = customer, BillingAddress = customer.BillingAddress,ForumTopic = forumPost.ForumTopic,
                    Forum = forumPost.ForumTopic.Forum, ForumPost = forumPost, FriendlyForumTopicPageIndex = friendlyForumTopicPageIndex },
                customer.Email, customer.GetFullName());
        }

        /// <summary>
        /// Sends a private message notification
        /// </summary>
        /// <param name="privateMessage">Private message</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public int SendPrivateMessageNotification(PrivateMessage privateMessage, int languageId)
        {
            return SendNotification(0, languageId, "Customer.NewPM",
                new TokenModel { Customer = privateMessage.ToCustomer,
                    BillingAddress = privateMessage.ToCustomer.BillingAddress, PrivateMessage = privateMessage },
                privateMessage.ToCustomer.Email, privateMessage.ToCustomer.GetFullName());
        }

        #endregion

        #region Misc

        /// <summary>
        /// Sends 'New vendor account submitted' message to a store owner
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="vendor">Vendor</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendNewVendorAccountApplyStoreOwnerNotification(Customer customer, Vendor vendor, int languageId)
        {
            return SendNotification(0, languageId, "VendorAccountApply.StoreOwnerNotification",
                new TokenModel { Customer = customer, Vendor = vendor });
        }

        /// <summary>
        /// Sends 'Vendor information changed' message to a store owner
        /// </summary>
        /// <param name="vendor">Vendor</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendVendorInformationChangeNotification(Vendor vendor, int languageId)
        {
            return SendNotification(0, languageId, "VendorInformationChange.StoreOwnerNotification",
                new TokenModel { Vendor = vendor });
        }

        /// <summary>
        /// Sends a gift card notification
        /// </summary>
        /// <param name="giftCard">Gift card</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendGiftCardNotification(GiftCard giftCard, int languageId)
        {
            return SendNotification(giftCard.PurchasedWithOrderItem != null ? giftCard.PurchasedWithOrderItem.Order.StoreId : 0, 
                languageId, "GiftCard.Notification", new TokenModel { GiftCard = giftCard },
                giftCard.RecipientEmail, giftCard.RecipientName);
        }

        /// <summary>
        /// Sends a product review notification message to a store owner
        /// </summary>
        /// <param name="productReview">Product review</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendProductReviewNotificationMessage(ProductReview productReview, int languageId)
        {
            return SendNotification(0, languageId, "Product.ProductReview",
                new TokenModel { Customer = productReview.Customer, ProductReview = productReview });
        }

        /// <summary>
        /// Sends a "quantity below" notification to a store owner
        /// </summary>
        /// <param name="product">Product</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendQuantityBelowStoreOwnerNotification(Product product, int languageId)
        {
            return SendNotification(0, languageId, "QuantityBelow.StoreOwnerNotification",
                new TokenModel { Product = product });
        }

        /// <summary>
        /// Sends a "quantity below" notification to a store owner
        /// </summary>
        /// <param name="combination">Attribute combination</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendQuantityBelowStoreOwnerNotification(ProductAttributeCombination combination, int languageId)
        {
            return SendNotification(0, languageId, "QuantityBelow.AttributeCombination.StoreOwnerNotification",
                new TokenModel { Product = combination.Product, Combination = combination });
        }

        /// <summary>
        /// Sends a "new VAT submitted" notification to a store owner
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="vatName">Received VAT name</param>
        /// <param name="vatAddress">Received VAT address</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendNewVatSubmittedStoreOwnerNotification(Customer customer,
            string vatName, string vatAddress, int languageId)
        {
            return SendNotification(0, languageId, "NewVATSubmitted.StoreOwnerNotification",
                new TokenModel { Tokens = new List<Token> { new Token("VatValidationResult.Name", vatName),
                    new Token("VatValidationResult.Address", vatAddress) },
                Customer = customer });
        }

        /// <summary>
        /// Sends a blog comment notification message to a store owner
        /// </summary>
        /// <param name="blogComment">Blog comment</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendBlogCommentNotificationMessage(BlogComment blogComment, int languageId)
        {
            return SendNotification(0, languageId, "Blog.BlogComment",
                new TokenModel { Customer = blogComment.Customer, BlogComment = blogComment });
        }

        /// <summary>
        /// Sends a news comment notification message to a store owner
        /// </summary>
        /// <param name="newsComment">News comment</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendNewsCommentNotificationMessage(NewsComment newsComment, int languageId)
        {
            return SendNotification(0, languageId, "News.NewsComment",
                new TokenModel { Customer = newsComment.Customer, NewsComment = newsComment });
        }

        /// <summary>
        /// Sends a 'Back in stock' notification message to a customer
        /// </summary>
        /// <param name="subscription">Subscription</param>
        /// <param name="languageId">Message language identifier</param>
        /// <returns>Queued email identifier</returns>
        public override int SendBackInStockNotification(BackInStockSubscription subscription, int languageId)
        {
            return SendNotification(subscription.StoreId, languageId, "Customer.BackInStock",
                new TokenModel { Customer = subscription.Customer,
                    BillingAddress = subscription.Customer.BillingAddress, Subscription = subscription },
                subscription.Customer.Email, subscription.Customer.GetFullName());
        }

        #endregion

        #endregion

        #region Nested class

        /// <summary>
        /// Token model
        /// </summary>
        protected class TokenModel
        {
            #region Ctor

            public TokenModel()
            {
                Tokens = new List<Token>();
            }

            #endregion

            #region Properties

            /// <summary>
            /// Gets or sets a list of tokens
            /// </summary>
            public IList<Token> Tokens { get; set; }

            /// <summary>
            /// Gets or sets a customer
            /// </summary>
            public Customer Customer { get; set; }

            /// <summary>
            /// Gets or sets a biiling address
            /// </summary>
            public Address BillingAddress { get; set; }

            /// <summary>
            /// Gets or sets an order
            /// </summary>
            public Order Order { get; set; }

            /// <summary>
            /// Gets or sets a shipment
            /// </summary>
            public Shipment Shipment { get; set; }

            /// <summary>
            /// Gets or sets a value of refunded amount
            /// </summary>
            public decimal RefundedAmount { get; set; }

            /// <summary>
            /// Gets or sets an order note 
            /// </summary>
            public OrderNote OrderNote { get; set; }

            /// <summary>
            /// Gets or sets a recurring payment
            /// </summary>
            public RecurringPayment RecurringPayment { get; set; }

            /// <summary>
            /// Gets or sets a return request
            /// </summary>
            public ReturnRequest ReturnRequest { get; set; }

            /// <summary>
            /// Gets or sets an order item 
            /// </summary>
            public OrderItem OrderItem { get; set; }

            /// <summary>
            /// Gets or sets a forum object
            /// </summary>
            public Forum Forum { get; set; }

            /// <summary>
            /// Gets or sets a forum topic
            /// </summary>
            public ForumTopic ForumTopic { get; set; }

            /// <summary>
            /// Gets or sets a forum post
            /// </summary>
            public ForumPost ForumPost { get; set; }

            /// <summary>
            /// Gets or sets a page index 
            /// </summary>
            public int FriendlyForumTopicPageIndex { get; set; }

            /// <summary>
            /// Gets or sets a private message
            /// </summary>
            public PrivateMessage PrivateMessage { get; set; }

            /// <summary>
            /// Gets or sets a vendor
            /// </summary>
            public Vendor Vendor { get; set; }

            /// <summary>
            /// Gets or sets a gift card
            /// </summary>
            public GiftCard GiftCard { get; set; }

            /// <summary>
            /// Gets or sets a product review
            /// </summary>
            public ProductReview ProductReview { get; set; }

            /// <summary>
            /// Gets or sets a product
            /// </summary>
            public Product Product { get; set; }

            /// <summary>
            /// Gets or sets a product attibute combination
            /// </summary>
            public ProductAttributeCombination Combination { get; set; }

            /// <summary>
            /// Gets or sets a blog comment
            /// </summary>
            public BlogComment BlogComment { get; set; }

            /// <summary>
            /// Gets or sets a news comment
            /// </summary>
            public NewsComment NewsComment { get; set; }

            /// <summary>
            /// Gets or sets a back in stock subscription  
            /// </summary>
            public BackInStockSubscription Subscription { get; set; }

            /// <summary>
            /// Gets or sets a newsletter subscription  
            /// </summary>
            public NewsLetterSubscription NewsLetterSubscription { get; set; }

            #endregion
        }

        #endregion
    }
}
